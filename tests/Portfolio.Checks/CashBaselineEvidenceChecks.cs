using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class CashBaselineEvidenceChecks
{
    public static async Task Run(TradingState retired, string account, DateTimeOffset soldAt)
    {
        var state = retired with
        {
            Mode = TradingMode.Stopped,
            Mandate = new TradingMandate(
                Guid.NewGuid(),
                account,
                retired.RulesHash,
                soldAt.AddHours(-1),
                soldAt.AddHours(1),
                soldAt
            ),
        };
        await EquivalentRepresentations(state, account, soldAt);
        await ChangedEvidence(state, account, soldAt);
        await LegacyProofAndRetries(state, account, soldAt);
        Console.WriteLine(
            "Cash baseline evidence: equivalent scales/offsets, material changes and retained legacy proof retries passed."
        );
    }

    private static async Task EquivalentRepresentations(TradingState state, string account, DateTimeOffset soldAt)
    {
        foreach (var scaledPreview in new[] { false, true })
        {
            var clock = new OrderCheckTime { Now = soldAt.AddDays(1) };
            var store = new FakeTradingStore(state);
            var broker = new FakeRiskEvidenceReader(Evidence(clock.Now, scaledPreview));
            var proofs = new ManualSaleReviewProof(new TradingControlOptions("synthetic-cash-evidence-owner"));
            using var gate = new SemaphoreSlim(1, 1);
            var workflow = new CashBaselineReviewService(
                new AccountContext(account),
                store,
                new FakeOrderStore(),
                broker,
                proofs,
                clock,
                gate
            );
            var preview = await workflow.Preview(CancellationToken.None);
            Check(store.History.Count == 1 && !preview.Applied, "Equivalent evidence preview never writes state.");
            broker.Evidence = Evidence(clock.Now, !scaledPreview);
            var result = await workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None);
            Check(
                result.Applied
                    && store.History.Count == 2
                    && store.State.Live.CashThb == 2000.25m
                    && store.State.Live.FundingThb == state.Live.FundingThb
                    && store.State.Live.ExternalSales.SequenceEqual(state.Live.ExternalSales)
                    && store.State.Live.Fills.SequenceEqual(state.Live.Fills)
                    && store.State.Mode == TradingMode.Stopped
                    && store.State.Mandate == state.Mandate,
                "Equivalent decimal scales, same-instant offsets and reordered balances/flows adopt once in both directions without authority or history changes."
            );
        }
    }

    private static async Task ChangedEvidence(TradingState state, string account, DateTimeOffset soldAt)
    {
        var clock = new OrderCheckTime { Now = soldAt.AddDays(1) };
        var evidence = Evidence(clock.Now, false);
        var flow = evidence.CashFlows[0];
        var crypto = evidence.CashFlows[1];
        var store = new FakeTradingStore(state);
        var broker = new FakeRiskEvidenceReader(evidence);
        var proofs = new ManualSaleReviewProof(new TradingControlOptions("synthetic-cash-evidence-owner"));
        using var gate = new SemaphoreSlim(1, 1);
        var workflow = new CashBaselineReviewService(
            new AccountContext(account),
            store,
            new FakeOrderStore(),
            broker,
            proofs,
            clock,
            gate
        );
        var preview = await workflow.Preview(CancellationToken.None);
        (string Name, RiskAccountEvidence Evidence)[] changes =
        [
            (
                "changed cash amount",
                evidence with
                {
                    Balances = [new Balance("THB", 2000.26m, 2000.26m), new Balance("WLD", 0m, 0m)],
                }
            ),
            ("changed flow quantity", evidence with { CashFlows = [flow, crypto with { Quantity = 0.126m }] }),
            ("changed flow fee", evidence with { CashFlows = [flow, crypto with { FeeQuantity = 0.001m }] }),
            ("changed flow valuation", evidence with { CashFlows = [flow with { ValueThb = 250.51m }, crypto] }),
            ("changed flow kind", evidence with { CashFlows = [flow, crypto with { Kind = CashFlowKind.Deposit }] }),
            ("changed flow identity", evidence with { CashFlows = [flow with { Id = "different-flow" }, crypto] }),
            ("changed flow instant", evidence with { CashFlows = [flow with { At = flow.At.AddSeconds(1) }, crypto] }),
            ("unknown valuation changed to zero", evidence with { CashFlows = [flow, crypto with { ValueThb = 0m }] }),
            (
                "unknown available cash",
                evidence with
                {
                    Balances = [new Balance("THB", 2000.25m), new Balance("WLD", 0m, 0m)],
                }
            ),
            (
                "open broker order",
                evidence with
                {
                    OpenOrders = [new RiskOpenOrder("open-order", "BTC", TradeSide.Buy, 1m, 100m)],
                }
            ),
        ];
        var before = JsonSerializer.Serialize(state);
        foreach (var change in changes)
        {
            broker.Evidence = change.Evidence;
            await Reject(
                () => workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None),
                change.Name
            );
            Check(
                store.History.Count == 1 && JsonSerializer.Serialize(store.State) == before,
                $"Rejected {change.Name} leaves all state unchanged."
            );
        }

        broker.Evidence = evidence with { CashFlows = [flow, crypto with { ValueThb = 0m }] };
        var zeroPreview = await workflow.Preview(CancellationToken.None);
        broker.Evidence = evidence;
        await Reject(
            () => workflow.Apply(zeroPreview.Proof, zeroPreview.Acknowledgement, CancellationToken.None),
            "zero valuation changed to unknown"
        );
        Check(
            store.History.Count == 1 && JsonSerializer.Serialize(store.State) == before,
            "Zero and unknown valuations remain distinct in both directions."
        );
    }

    private static async Task LegacyProofAndRetries(TradingState state, string account, DateTimeOffset soldAt)
    {
        var clock = new OrderCheckTime { Now = soldAt.AddDays(1) };
        var evidence = Evidence(clock.Now, false);
        var store = new FakeTradingStore(state);
        var broker = new FakeRiskEvidenceReader(evidence);
        var proofs = new ManualSaleReviewProof(new TradingControlOptions("synthetic-cash-evidence-owner"));
        using var gate = new SemaphoreSlim(1, 1);
        var workflow = new CashBaselineReviewService(
            new AccountContext(account),
            store,
            new FakeOrderStore(),
            broker,
            proofs,
            clock,
            gate
        );
        var preview = await workflow.Preview(CancellationToken.None);
        var legacyHash = TradingCashBaselineAccounting.Hash(
            new
            {
                Balances = evidence.Balances.OrderBy(balance => balance.Symbol, StringComparer.Ordinal).ToArray(),
                evidence.OpenOrders,
                CashFlows = evidence.CashFlows.OrderBy(flow => flow.Id, StringComparer.Ordinal).ToArray(),
            }
        );
        var snapshot = proofs.ReadCashBaseline(preview.Proof);
        Check(
            snapshot.EvidenceHash != legacyHash,
            "Legacy fixture requires a distinct raw hash so acceptance exercises compatibility."
        );
        var legacyProof = proofs.ProtectCashBaseline(snapshot with { EvidenceHash = legacyHash });
        var applied = await workflow.Apply(legacyProof, preview.Acknowledgement, CancellationToken.None);
        Check(
            applied.Applied
                && store.History.Count == 2
                && store.State.Live.CashBaselines.Single().EvidenceHash == legacyHash,
            "An unchanged legacy raw-hash proof remains valid and retains its reviewed evidence identity."
        );
        var reads = broker.ReadCalls;
        var committed = JsonSerializer.Serialize(store.State);
        _ = await workflow.Apply(legacyProof, preview.Acknowledgement, CancellationToken.None);
        clock.Now = clock.Now.AddHours(1);
        broker.Evidence = evidence with { Balances = [] };
        var restarted = new CashBaselineReviewService(
            new AccountContext(account),
            store,
            new FakeOrderStore(),
            broker,
            proofs,
            clock,
            gate
        );
        _ = await restarted.Apply(legacyProof, preview.Acknowledgement, CancellationToken.None);
        Check(
            store.History.Count == 2
                && broker.ReadCalls == reads
                && JsonSerializer.Serialize(store.State) == committed
                && store.State.Live.CashBaselines.Single().EvidenceHash == legacyHash,
            "Repeated and expired committed legacy retries preserve state and hash without another broker read."
        );
    }

    private static RiskAccountEvidence Evidence(DateTimeOffset now, bool scaled)
    {
        var observedAt = scaled ? now.ToOffset(TimeSpan.FromHours(7)) : now;
        Balance[] balances =
        [
            new Balance("THB", scaled ? 2000.2500m : 2000.25m, scaled ? 2000.2500m : 2000.25m),
            new Balance("WLD", scaled ? 0.00000000m : 0m, scaled ? 0.00000000m : 0m),
        ];
        CapitalCashFlow[] flows =
        [
            new CapitalCashFlow(
                "cash-withdrawal",
                "THB",
                scaled ? 250.7500m : 250.75m,
                scaled ? 0.2500m : 0.25m,
                observedAt.AddHours(-2),
                CashFlowKind.Withdrawal,
                scaled ? 250.5000m : 250.50m
            ),
            new CapitalCashFlow(
                "crypto-withdrawal",
                "BTC",
                scaled ? 0.12500m : 0.125m,
                scaled ? 0.000m : 0m,
                observedAt.AddDays(-1),
                CashFlowKind.Withdrawal,
                null
            ),
        ];
        return new RiskAccountEvidence(
            observedAt,
            scaled ? balances.Reverse().ToArray() : balances,
            [],
            scaled ? flows.Reverse().ToArray() : flows,
            observedAt.AddDays(-90)
        );
    }

    private static async Task Reject<T>(Func<Task<T>> action, string name)
    {
        try
        {
            _ = await action();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected cash-baseline evidence rejection: {name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
