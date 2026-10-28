using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Infrastructure;
using Quantara.Portfolio.Infrastructure.Risk;

namespace Portfolio.Checks;

public static class CompanyCapitalImportChecks
{
    public static async Task Run()
    {
        var root = Directory.CreateTempSubdirectory("quantara-company-evidence-").FullName;
        try
        {
            await FundingAndRestart(Path.Combine(root, "funding.json"));
            await CompanyDecimalChecks.Run(Path.Combine(root, "precision.json"));
            await BrokerReaderFixture(Path.Combine(root, "broker.json"));
            await TransferClassification(Path.Combine(root, "transfers.json"));
            await RejectsCorruptionAndCompetingWrites(Path.Combine(root, "recovery.json"));
            Console.WriteLine(
                "Company evidence import: provenance, monthly deduplication, conflicts, classification, scope, revision guards and restart recovery passed; offline fixtures only."
            );
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root))
            {
                File.Delete(file);
            }

            Directory.Delete(root);
        }
    }

    private static async Task FundingAndRestart(string path)
    {
        var a = Account("bitkub", "a");
        var funding = Flow("monthly-funding", 500, CashFlowKind.Deposit);
        var import = Batch(a, "january", 1000, 1530, [funding, funding]);
        var store = new CompanyCapitalEvidenceStore(path, [a]);
        Check(
            (await store.Read(CancellationToken.None)).Revision == 0 && !File.Exists(path),
            "An empty journal read has no persistence side effect."
        );
        var first = await store.Import(import, 0, CancellationToken.None);
        Check(
            first.Revision == 1 && first.Receipts.Count == 1,
            "A complete import and its provenance are published once."
        );
        var restarted = new CompanyCapitalEvidenceStore(path, [a]);
        var repeated = await restarted.Import(import, 0, CancellationToken.None);
        Check(
            repeated.Revision == 1 && repeated.Receipts[0].Digest == first.Receipts[0].Digest,
            "Restart and a stale retry return the same import receipt."
        );
        var offset = TimeSpan.FromHours(7);
        var equivalent = import with
        {
            Period = import.Period with
            {
                Previous = Snapshot("a", import.Period.Previous.ObservedAt.ToOffset(offset), 1000.00m),
                Current = Snapshot("a", import.Period.Current.ObservedAt.ToOffset(offset), 1530.00m),
                CoverageFrom = import.Period.CoverageFrom.ToOffset(offset),
                Flows =
                [
                    funding with
                    {
                        At = funding.At.ToOffset(offset),
                        Quantity = 500.00m,
                        FeeQuantity = 0.00m,
                        ValueThb = 500.00m,
                    },
                ],
            },
        };
        Check(
            (await restarted.Import(equivalent, 0, CancellationToken.None)).Revision == 1,
            "Equivalent decimal scales, duplicate ordering and UTC offsets replay as the same evidence."
        );
        await Rejects(
            () =>
                restarted.Import(
                    import with
                    {
                        Id = "incoherent-equity",
                        Period = import.Period with
                        {
                            Current = import.Period.Current with
                            {
                                ObservedAt = import.Period.Current.ObservedAt.AddDays(1),
                                EquityThb = 1700,
                            },
                        },
                    },
                    1,
                    CancellationToken.None
                ),
            "A new observation cannot claim equity that contradicts its marked assets."
        );
        await Rejects(
            () =>
                restarted.Import(
                    import with
                    {
                        Id = "missing-price",
                        Period = import.Period with
                        {
                            Current = import.Period.Current with
                            {
                                ObservedAt = import.Period.Current.ObservedAt.AddDays(1),
                                Assets = [new("THB", 1530, 1530, null, 0)],
                            },
                        },
                    },
                    1,
                    CancellationToken.None
                ),
            "Known equity cannot hide a missing held-asset price."
        );
        var report = await restarted.Report(
            import.Period.Previous.ObservedAt,
            import.Period.Current.ObservedAt,
            CancellationToken.None
        );
        Check(
            report?.DepositsThb == 500 && report.NetEquityChangeThb == 30 && !report.VerifiedIncome,
            "Monthly capital stays separate from marked equity change after restart."
        );
        Check(
            report is { EvidenceOrigin: "offlineFixture", ImportRevision: 1 }
                && report.ImportDigests.SequenceEqual([first.Receipts[0].Digest]),
            "The report binds its fixture origin and exact import revision to retained evidence."
        );
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        await Rejects(
            () => restarted.Import(import with { SourceReference = "changed-source" }, 1, CancellationToken.None),
            "An import identity cannot replace its source evidence."
        );
        await Rejects(
            () =>
                restarted.Import(
                    import with
                    {
                        Id = "changed-flow",
                        Period = import.Period with { Flows = [funding with { Quantity = 700, ValueThb = 700 }] },
                    },
                    1,
                    CancellationToken.None
                ),
            "A new import ID cannot rewrite an existing broker movement."
        );
        await Rejects(
            () =>
                restarted.Import(
                    import with
                    {
                        Id = "changed-snapshot",
                        Period = import.Period with { Current = Snapshot("a", import.Period.Current.ObservedAt, 1700) },
                    },
                    1,
                    CancellationToken.None
                ),
            "Same-time wallet evidence cannot be edited by another import."
        );
        await Rejects(
            () => restarted.Import(import with { Id = "stale-revision" }, 0, CancellationToken.None),
            "A stale writer cannot append a different import."
        );
        var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
        Check(before.SequenceEqual(after), "Rejected imports leave the committed journal unchanged.");
        await Rejects(
            () => new CompanyCapitalEvidenceStore(path, [a with { LegalRegion = "US" }]).Read(CancellationToken.None),
            "Restart cannot switch legal region or account scope."
        );
        await Rejects(
            () =>
                store.Import(
                    import with
                    {
                        Id = "live",
                        Source = a with { Environment = "live" },
                    },
                    1,
                    CancellationToken.None
                ),
            "The fixture journal rejects live-source claims."
        );

        var unknown = import with
        {
            Id = "unclassified",
            Period = import.Period with { ExternalFlowEvidence = new Dictionary<string, string>() },
        };
        var classificationPath = path + ".classification";
        var classifier = new CompanyCapitalEvidenceStore(classificationPath, [a]);
        await classifier.Import(unknown, 0, CancellationToken.None);
        Check(
            (
                await classifier.Report(
                    import.Period.Previous.ObservedAt,
                    import.Period.Current.ObservedAt,
                    CancellationToken.None
                )
            )?.NetEquityChangeThb
                is null,
            "Unclassified funding is not silently promoted to profit."
        );
        await classifier.Import(import with { Id = "classification-proof" }, 1, CancellationToken.None);
        var classified = await new CompanyCapitalEvidenceStore(classificationPath, [a]).Report(
            import.Period.Previous.ObservedAt,
            import.Period.Current.ObservedAt,
            CancellationToken.None
        );
        Check(
            classified?.NetContributionsThb == 500 && classified.FlowCount == 1,
            "New classification evidence is retained separately without duplicating the broker movement."
        );
    }

    private static async Task TransferClassification(string path)
    {
        var a = Account("bitkub", "a");
        var b = Account("webull", "b");
        var funding = Flow("monthly-funding", 500, CashFlowKind.Deposit);
        var withdrawal = Flow("out", 201, CashFlowKind.Withdrawal) with { FeeQuantity = 1, ValueThb = 200 };
        var deposit = Flow("in", 200, CashFlowKind.Deposit);
        var outgoing = Batch(a, "transfer", 1000, 1329, [funding, withdrawal]) with
        {
            Transfers = [new("a", "out", "b", "broker-transfer-reference")],
        };
        outgoing = outgoing with
        {
            Period = outgoing.Period with
            {
                ExternalFlowEvidence = new Dictionary<string, string> { [funding.Id] = "fixture-bank-funding" },
            },
        };
        var incoming = Batch(b, "transfer", 500, 710, [deposit]) with
        {
            Transfers = [new("b", "in", "a", "broker-transfer-reference")],
        };
        incoming = incoming with
        {
            Period = incoming.Period with { ExternalFlowEvidence = new Dictionary<string, string>() },
        };
        var store = new CompanyCapitalEvidenceStore(path, [a, b]);
        await store.Import(outgoing, 0, CancellationToken.None);
        Check(
            (
                await store.Report(
                    outgoing.Period.Previous.ObservedAt,
                    outgoing.Period.Current.ObservedAt,
                    CancellationToken.None
                )
            )?.NetEquityChangeThb
                is null,
            "An unmatched transfer and missing configured account remain Unknown."
        );
        await new CompanyCapitalEvidenceStore(path, [a, b]).Import(incoming, 1, CancellationToken.None);
        var restarted = new CompanyCapitalEvidenceStore(path, [a, b]);
        var report = await restarted.Report(
            outgoing.Period.Previous.ObservedAt,
            outgoing.Period.Current.ObservedAt,
            CancellationToken.None
        );
        Check(
            report
                is { DepositsThb: 500, InternalTransferCount: 1, InternalTransferFeesThb: 1, NetEquityChangeThb: 39 },
            "Mutual broker references classify a transfer after both offline fixtures arrive."
        );
        var retry = await restarted.Import(incoming, 0, CancellationToken.None);
        Check(
            retry.Revision == 2,
            "Identical IDs may exist on different accounts; a repeated account import is idempotent."
        );
        await Rejects(
            () =>
                restarted.Import(
                    incoming with
                    {
                        Id = "bad-classification",
                        Period = incoming.Period with
                        {
                            ExternalFlowEvidence = new Dictionary<string, string> { [deposit.Id] = "external-proof" },
                        },
                    },
                    2,
                    CancellationToken.None
                ),
            "An internal leg cannot later become external capital."
        );

        var unmatchedPath = path + ".unmatched";
        var unmatched = new CompanyCapitalEvidenceStore(unmatchedPath, [a, b]);
        await unmatched.Import(outgoing, 0, CancellationToken.None);
        await unmatched.Import(
            incoming with
            {
                Transfers = [new("b", "in", "a", "different-reference")],
            },
            1,
            CancellationToken.None
        );
        Check(
            (
                await unmatched.Report(
                    outgoing.Period.Previous.ObservedAt,
                    outgoing.Period.Current.ObservedAt,
                    CancellationToken.None
                )
            )?.NetEquityChangeThb
                is null,
            "Similar amounts and times never substitute for a matching transfer reference."
        );
        var extra = incoming.Period with
        {
            Previous = Snapshot("c", incoming.Period.Previous.ObservedAt, 500),
            Current = Snapshot("c", incoming.Period.Current.ObservedAt, 700),
            Flows = [deposit with { Id = "extra-in" }],
        };
        Check(
            CompanyTransferClassifier
                .Match(
                    [outgoing.Period, incoming.Period, extra],
                    [
                        .. outgoing.Transfers,
                        .. incoming.Transfers,
                        new("c", "extra-in", "a", "broker-transfer-reference"),
                    ]
                )
                .Length == 0,
            "An ambiguous reference with three legs cannot select a convenient pair."
        );
    }

    private static async Task RejectsCorruptionAndCompetingWrites(string path)
    {
        var a = Account("bitkub", "a");
        var batch = Batch(a, "initial", 1000, 1000, []);
        var store = new CompanyCapitalEvidenceStore(path, [a]);
        await store.Import(batch, 0, CancellationToken.None);
        await File.WriteAllTextAsync(path + ".orphan.tmp", "{partial");
        Check(
            (await new CompanyCapitalEvidenceStore(path, [a]).Read(CancellationToken.None)).Revision == 1,
            "Restart ignores an unpublished temporary payload."
        );
        await using (
            var held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
        )
        {
            await Rejects(
                () =>
                    new CompanyCapitalEvidenceStore(path, [a]).Import(
                        batch with
                        {
                            Id = "competing",
                        },
                        1,
                        CancellationToken.None
                    ),
                "A competing writer fails without replacing the journal."
            );
        }

        await store.Import(batch with { Id = "after-lock" }, 1, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Rejects(
            () => store.Import(batch with { Id = "cancelled" }, 2, cancellation.Token),
            "Cancellation cannot publish a new import."
        );
        var valid = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(
            path,
            valid.Replace("fixture-observation", "tampered-observation", StringComparison.Ordinal)
        );
        await Rejects(
            () => new CompanyCapitalEvidenceStore(path, [a]).Read(CancellationToken.None),
            "A changed payload cannot pass its retained content digest."
        );
        var invalid = JsonNode.Parse(valid) ?? throw new InvalidOperationException("Fixture JSON is unavailable.");
        invalid["Receipts"] = new JsonArray((JsonNode?)null);
        invalid["Revision"] = 1;
        await File.WriteAllTextAsync(path, invalid.ToJsonString());
        await Rejects(
            () => new CompanyCapitalEvidenceStore(path, [a]).Read(CancellationToken.None),
            "Null receipt entries are rejected as malformed retained evidence."
        );
        await File.WriteAllTextAsync(path, valid.Replace("+00:00\"", "\"", StringComparison.Ordinal));
        await Rejects(
            () => new CompanyCapitalEvidenceStore(path, [a]).Read(CancellationToken.None),
            "An offset-free stored timestamp cannot inherit the machine timezone."
        );
    }

    private static async Task BrokerReaderFixture(string path)
    {
        using var http = new HttpClient(new RiskEvidenceHttpHandler())
        {
            BaseAddress = new Uri("https://api.bitkub.com"),
        };
        var reader = new BitkubRiskEvidenceReader(
            new BitkubAccountReader(http, new("risk-key", "risk-secret")),
            TimeProvider.System
        );
        var evidence = await reader.Read(CancellationToken.None);
        var account = Account("bitkub", "parsed-fixture");
        var assets = evidence
            .Balances.Select(balance => new CapitalAsset(
                balance.Symbol,
                balance.Quantity,
                balance.Available,
                balance.Symbol == "THB" ? 1 : null,
                0
            ))
            .ToArray();
        var current = new CapitalSnapshot(
            account.AccountKey,
            evidence.ObservedAt,
            null,
            null,
            null,
            null,
            assets,
            false,
            null
        )
        {
            Source = CapitalSource.Broker,
        };
        var period = new CompanyAccountPeriod(
            Snapshot(account.AccountKey, new DateTimeOffset(2023, 11, 1, 0, 0, 0, TimeSpan.Zero), 1000),
            current,
            evidence.CashFlows,
            evidence.CashFlowCoverageFrom,
            new Dictionary<string, string>()
        );
        var store = new CompanyCapitalEvidenceStore(path, [account]);
        var retained = await store.Import(
            new("parsed-reader", account, "company-capital-fixture-v1", "fixture:RiskEvidenceHttpHandler", period, []),
            0,
            CancellationToken.None
        );
        Check(
            retained.Receipts[0].Import.Period.Flows.Count == 4,
            "Completed fiat and crypto broker-parser fixtures enter the same durable evidence path."
        );
        Check(
            (
                await new CompanyCapitalEvidenceStore(path, [account]).Report(
                    period.Previous.ObservedAt,
                    period.Current.ObservedAt,
                    CancellationToken.None
                )
            )?.NetEquityChangeThb
                is null,
            "Incomplete parsed broker coverage, prices and counterparties remain Unknown after restart."
        );
    }

    private static CompanyBrokerAccount Account(string broker, string key) => new(broker, "TH", key, "offlineFixture");

    private static CapitalCashFlow Flow(string id, decimal value, CashFlowKind kind) =>
        new(id, "THB", value, 0, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), kind, value);

    private static CompanyCapitalImport Batch(
        CompanyBrokerAccount account,
        string id,
        decimal opening,
        decimal closing,
        IReadOnlyList<CapitalCashFlow> flows
    )
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new(
            id,
            account,
            "company-capital-fixture-v1",
            "fixture-observation",
            new(
                Snapshot(account.AccountKey, from, opening),
                Snapshot(account.AccountKey, from.AddMonths(1), closing),
                flows,
                from,
                flows
                    .DistinctBy(flow => flow.Id)
                    .ToDictionary(flow => flow.Id, _ => "fixture-bank-funding", StringComparer.Ordinal)
            ),
            []
        );
    }

    private static CapitalSnapshot Snapshot(string account, DateTimeOffset at, decimal equity) =>
        new(account, at, equity, equity, 0, 0, [new("THB", equity, equity, 1, 0)], false, null)
        {
            Source = CapitalSource.Broker,
        };

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task Rejects(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
            when (exception
                    is InvalidDataException
                        or InvalidOperationException
                        or IOException
                        or OperationCanceledException
            )
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
