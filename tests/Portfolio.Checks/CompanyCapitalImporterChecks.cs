using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;
using Quantara.Portfolio.Infrastructure.Risk;

namespace Portfolio.Checks;

public static class CompanyCapitalImporterChecks
{
    public static async Task Run()
    {
        var root = Directory.CreateTempSubdirectory("quantara-company-importer-").FullName;
        try
        {
            await AdmittedReadOnlyPath(Path.Combine(root, "admitted.json"));
            await IncompleteEvidence(Path.Combine(root, "incomplete.json"));
            await SourceAdmission(Path.Combine(root, "source.json"));
            await ConcurrentReport(Path.Combine(root, "concurrent.json"));
            Console.WriteLine(
                "Company importer: existing read-only reader integration, source/account admission, coherent valuation, unclassified funding, freshness and simulation-only readiness passed."
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

    private static async Task AdmittedReadOnlyPath(string path)
    {
        var time = new OrderCheckTime();
        var account = new AccountContext(new string('a', 64));
        var source = new CompanyBrokerAccount("bitkub", "TH", account.AccountKey, "offlineFixture");
        var reader = new FakeRiskEvidenceReader(new(time.Now, [new("THB", 1000, 1000)], [], [], time.Now.AddDays(-1)));
        var risk = await Baseline(account.AccountKey, time.Now.AddMinutes(-1));
        var store = new CompanyCapitalEvidenceStore(path, [source]);
        var importer = Importer(reader, risk, store, account, time, []);
        var result = await importer.Import(source, CancellationToken.None);
        Check(
            result.Readiness.CanSimulate && !result.Readiness.CanUseForLiveDecisions,
            "Complete admitted evidence can initialize simulation without granting live authority."
        );
        Check(
            result.Report is { ClosingEquityThb: 1000, EvidenceOrigin: "offlineFixture", ImportRevision: 1 },
            "The importer uses actual reader valuations and journal provenance."
        );
        var repeated = await importer.Import(source, CancellationToken.None);
        Check(
            repeated.Revision == 1 && repeated.ImportId == result.ImportId,
            "Repeated source observations reuse the durable receipt."
        );
        var restarted = Importer(reader, risk, new CompanyCapitalEvidenceStore(path, [source]), account, time, []);
        Check(
            (await restarted.Import(source, CancellationToken.None)).Revision == 1,
            "Application integration remains idempotent after restart."
        );
        Check(
            !CompanyCapitalReadinessGate
                .Inspect(result.Report, [account.AccountKey], time.Now.AddMinutes(2), TimeSpan.FromSeconds(90))
                .CanSimulate,
            "Stale results cannot feed the company simulation."
        );
        Check(
            !CompanyCapitalReadinessGate
                .Inspect(result.Report, ["other"], time.Now, TimeSpan.FromSeconds(90))
                .CanSimulate,
            "The consumer gate rechecks company account scope."
        );
        var report = result.Report ?? throw new InvalidOperationException("Expected reconciled fixture report.");
        foreach (
            var invalid in new[]
            {
                report with
                {
                    NetEquityChangeThb = 1,
                },
                report with
                {
                    NetContributionsThb = 1,
                },
                report with
                {
                    DepositsThb = null,
                },
                report with
                {
                    WithdrawalsThb = -1,
                },
                report with
                {
                    OpeningEquityThb = -1,
                    NetEquityChangeThb = 1001,
                },
                report with
                {
                    InternalTransferFeesThb = -1,
                },
                report with
                {
                    InternalTransferNetThb = null,
                },
            }
        )
        {
            Check(
                !CompanyCapitalReadinessGate
                    .Inspect(invalid, [account.AccountKey], time.Now, TimeSpan.FromSeconds(90))
                    .CanSimulate,
                "Missing or inconsistent capital totals cannot reach company simulation."
            );
        }
    }

    private static async Task IncompleteEvidence(string path)
    {
        var time = new OrderCheckTime();
        var account = new AccountContext(new string('a', 64));
        var source = new CompanyBrokerAccount("bitkub", "TH", account.AccountKey, "offlineFixture");
        var funding = new CapitalCashFlow(
            "new-monthly-funding",
            "THB",
            500,
            0,
            time.Now.AddSeconds(-30),
            CashFlowKind.Deposit,
            500
        );
        var reader = new FakeRiskEvidenceReader(
            new(time.Now, [new("THB", 1500, 1500)], [], [funding], time.Now.AddDays(-1))
        );
        var store = new CompanyCapitalEvidenceStore(path, [source]);
        var risk = await Baseline(account.AccountKey, time.Now.AddMinutes(-1));
        var importer = Importer(reader, risk, store, account, time, []);
        var result = await importer.Import(source, CancellationToken.None);
        Check(
            !result.Readiness.CanSimulate && result.Report?.NetEquityChangeThb is null && result.Revision == 1,
            "A detected monthly deposit is retained but not guessed to be classified external capital or profit."
        );
        var receipt = (await store.Read(CancellationToken.None)).Receipts.Single();
        Check(
            receipt.Import.Period.Flows.Single() == funding && receipt.Import.Period.ExternalFlowEvidence.Count == 0,
            "The bridge preserves broker movements without inventing counterparties."
        );

        var missing = Importer(
            reader,
            new FakeRiskStore(),
            new CompanyCapitalEvidenceStore(path + ".baseline", [source]),
            account,
            time,
            []
        );
        Check(
            !(await missing.Import(source, CancellationToken.None)).Readiness.CanSimulate
                && !File.Exists(path + ".baseline"),
            "Missing opening evidence defers import without fabricating a balance."
        );
        var held = new FakeRiskEvidenceReader(
            new(time.Now, [new("THB", 1000, 1000), new("BTC", 1, 1)], [], [], time.Now.AddDays(-1))
        );
        var unpriced = Importer(
            held,
            risk,
            new CompanyCapitalEvidenceStore(path + ".price", [source]),
            account,
            time,
            []
        );
        var unpricedResult = await unpriced.Import(source, CancellationToken.None);
        Check(
            !unpricedResult.Readiness.CanSimulate && unpricedResult.Report?.ClosingEquityThb is null,
            "Missing held-asset valuation cannot reach company decisions."
        );
        var futureBook = new TradingBook("BTC", time.Now.AddSeconds(1), 100, 101, 1, 1, []);
        var late = Importer(
            held,
            risk,
            new CompanyCapitalEvidenceStore(path + ".cutoff", [source]),
            account,
            time,
            [futureBook]
        );
        Check(
            !(await late.Import(source, CancellationToken.None)).Readiness.CanSimulate
                && !File.Exists(path + ".cutoff"),
            "A later quote cannot be relabeled as an earlier coherent account observation."
        );
    }

    private static async Task SourceAdmission(string path)
    {
        var time = new OrderCheckTime();
        var account = new AccountContext(new string('a', 64));
        var source = new CompanyBrokerAccount("bitkub", "TH", account.AccountKey, "offlineFixture");
        var reader = new FakeRiskEvidenceReader(new(time.Now, [new("THB", 1000, 1000)], [], [], time.Now.AddDays(-1)));
        var risk = await Baseline(account.AccountKey, time.Now.AddMinutes(-1));
        var store = new CompanyCapitalEvidenceStore(path, [source]);
        var importer = Importer(reader, risk, store, account, time, []);
        foreach (
            var rejected in new[]
            {
                source with
                {
                    LegalRegion = "US",
                },
                source with
                {
                    Broker = "other",
                },
                source with
                {
                    AccountKey = new string('b', 64),
                },
                source with
                {
                    Environment = "production",
                },
            }
        )
        {
            Check(
                !(await importer.Import(rejected, CancellationToken.None)).Readiness.CanSimulate
                    && reader.ReadCalls == 0,
                "Wrong source, region, account or environment is rejected before source I/O."
            );
        }

        reader.Origin = reader.Origin with { AdapterVersion = "unadmitted-adapter" };
        Check(
            !(await importer.Import(source, CancellationToken.None)).Readiness.CanSimulate && reader.ReadCalls == 0,
            "An unsupported adapter is rejected before source I/O."
        );
        reader.Origin = reader.Origin with { AdapterVersion = "bitkub-risk-read-only-v1" };
        var outsideScope = Importer(
            reader,
            risk,
            new CompanyCapitalEvidenceStore(path + ".scope", [source with { AccountKey = new string('b', 64) }]),
            account,
            time,
            []
        );
        Check(
            !(await outsideScope.Import(source, CancellationToken.None)).Readiness.CanSimulate && reader.ReadCalls == 0,
            "The configured journal scope is checked before source I/O."
        );
        reader.Origin = reader.Origin with { Environment = "production" };
        Check(
            !(await importer.Import(source, CancellationToken.None)).Readiness.CanSimulate && reader.ReadCalls == 0,
            "Production evidence cannot enter the fixture importer by changing a label."
        );
        Check(
            !(await importer.Import(source with { Environment = "production" }, CancellationToken.None))
                .Readiness
                .CanSimulate
                && reader.ReadCalls == 0,
            "A matching production source declaration does not grant fixture admission."
        );
        reader.Origin = reader.Origin with { Environment = "offlineFixture" };
        reader.BeforeRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = importer.Import(source, CancellationToken.None);
        reader.Origin = reader.Origin with { Broker = "other" };
        reader.BeforeRead.SetResult();
        Check(
            !(await pending).Readiness.CanSimulate && !File.Exists(path),
            "Source identity is checked again after asynchronous collection."
        );
    }

    private static async Task ConcurrentReport(string path)
    {
        var time = new OrderCheckTime();
        var account = new AccountContext(new string('a', 64));
        var source = new CompanyBrokerAccount("bitkub", "TH", account.AccountKey, "offlineFixture");
        var reader = new FakeRiskEvidenceReader(new(time.Now, [new("THB", 1000, 1000)], [], [], time.Now.AddDays(-1)));
        var risk = await Baseline(account.AccountKey, time.Now.AddMinutes(-1));
        var journal = new AdvancingCapitalEvidenceStore(new CompanyCapitalEvidenceStore(path, [source]));
        var importer = Importer(reader, risk, journal, account, time, []);
        var result = await importer.Import(source, CancellationToken.None);
        Check(
            result.Readiness.CanSimulate && result.Revision == 2 && result.Report?.ImportRevision == result.Revision,
            "A concurrent append cannot separate the returned report from its evidence revision."
        );
    }

    private static CompanyCapitalImporter Importer(
        FakeRiskEvidenceReader reader,
        IReadOnlyRiskStore risk,
        ICompanyCapitalEvidenceStore journal,
        IAccountContext account,
        TimeProvider time,
        IReadOnlyList<TradingBook> books
    )
    {
        var capital = new RiskCapitalReader(
            reader,
            new FakeOrderStore(),
            new FakeTradingStore(TradingStateFactory.Create(time.GetUtcNow())),
            new FakeTradingMarketReader(books),
            account,
            time
        );
        return new(capital, risk, journal, account, time);
    }

    private static async Task<FakeRiskStore> Baseline(string account, DateTimeOffset at)
    {
        var store = new FakeRiskStore();
        await store.Observe(
            new(account, at, 1000, 1000, 0, 0, [new("THB", 1000, 1000, 1, 0)], false, null)
            {
                Source = CapitalSource.Broker,
            },
            [],
            at,
            CancellationToken.None
        );
        return store;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
