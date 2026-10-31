using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class CashBaselineChecks
{
    public static async Task Run(TradingState retired, string account, DateTimeOffset soldAt)
    {
        var clock = new OrderCheckTime { Now = soldAt.AddDays(1) };
        var state = retired with
        {
            Mandate = new TradingMandate(
                Guid.NewGuid(),
                account,
                retired.RulesHash,
                soldAt.AddHours(-1),
                soldAt.AddHours(1),
                soldAt
            ),
        };
        var store = new FakeTradingStore(state);
        var evidence = new RiskAccountEvidence(
            clock.Now,
            [new Balance("THB", 5096.69m, 5096.69m), new Balance("WLD", 0m, 0m)],
            [],
            [],
            soldAt.AddDays(-90)
        );
        var broker = new FakeRiskEvidenceReader(evidence);
        var proofs = new ManualSaleReviewProof(new TradingControlOptions("synthetic-existing-owner"));
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
        var before = JsonSerializer.Serialize(state);
        var preview = await workflow.Preview(CancellationToken.None);
        Check(
            JsonSerializer.Serialize(store.State) == before && !preview.Applied,
            "Cash preview never writes accounting"
        );

        broker.Evidence = evidence with { Balances = [new Balance("THB", 5097m, 5097m), new Balance("WLD", 0m, 0m)] };
        await Reject(() => workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None));
        Check(store.History.Count == 1, "Changed broker evidence cannot be adopted");
        broker.Evidence = evidence;
        broker.BeforeRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applying = workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None);
        var competingWrite = gate.WaitAsync();
        Check(!competingWrite.IsCompleted, "Cash confirmation excludes concurrent engine writes");
        broker.BeforeRead.SetResult();
        var result = await applying;
        await competingWrite;
        gate.Release();
        broker.BeforeRead = null;
        var adopted = store.State;
        Check(
            result.Applied
                && adopted.Live.CashThb == 5096.69m
                && adopted.Live.FundingThb == state.Live.FundingThb
                && adopted.Live.ExternalSales.SequenceEqual(state.Live.ExternalSales)
                && adopted.Live.Fills.SequenceEqual(state.Live.Fills)
                && adopted.Rules == state.Rules
                && adopted.Mode == TradingMode.Stopped
                && adopted.Mandate == state.Mandate,
            "Cash replacement preserves funding, losses, historical uncertainty and disarmed authority"
        );
        Check(
            !TradingCashBaselineAccounting.CashUnknown(adopted.Live)
                && adopted.Live.ExternalSales.Single().Settlement is null
                && TradingLedgerAccounting.Equity(adopted.Live, [], clock.Now, 90) == 5096.69m,
            "Known present cash never fabricates historical settlement or profit"
        );

        var restarted = new CashBaselineReviewService(
            new AccountContext(account),
            store,
            new FakeOrderStore(),
            broker,
            proofs,
            clock,
            gate
        );
        clock.Now = clock.Now.AddHours(1);
        _ = await restarted.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None);
        Check(store.History.Count == 2, "Restart/idempotent retry does not write accounting or credit cash twice");

        var oldFill = new TradingFill(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "unseen-old-fill",
            "WLD",
            TradeSide.Buy,
            1m,
            10m,
            10m,
            0m,
            soldAt
        );
        await Reject(() => Task.FromResult(TradingLedgerAccounting.Apply(adopted.Live, oldFill)));
        var knownLedger = adopted.Live with { Fills = adopted.Live.Fills.Append(oldFill).ToArray() };
        Check(
            TradingLedgerAccounting.Apply(knownLedger, oldFill) == knownLedger,
            "Known fill remains a no-op before cutoff"
        );

        clock.Now = evidence.ObservedAt;
        var cas = new FakeTradingStore(state);
        var casWorkflow = new CashBaselineReviewService(
            new AccountContext(account),
            cas,
            new FakeOrderStore(),
            broker,
            proofs,
            clock,
            gate
        );
        var casPreview = await casWorkflow.Preview(CancellationToken.None);
        cas.BeforeSave = _ => cas.State = cas.State with { Revision = cas.State.Revision + 1 };
        await Reject(() => casWorkflow.Apply(casPreview.Proof, casPreview.Acknowledgement, CancellationToken.None));
        Check(
            cas.State.Live.CashBaselines.Count == 0 && cas.State.Live.CashThb == state.Live.CashThb,
            "Revision race leaves cash unchanged"
        );

        var baseline = adopted.Live.CashBaselines.Single();
        var concurrent = new CapitalCashFlow(
            "late-visible-deposit",
            "THB",
            10m,
            0m,
            baseline.BrokerObservedAt.AddSeconds(1),
            CashFlowKind.Deposit,
            10m
        );
        var collecting = adopted.Live with
        {
            CashBaselines =
            [
                baseline with
                {
                    Cutoff = baseline.BrokerObservedAt.AddSeconds(5),
                    RecordedAt = baseline.RecordedAt.AddSeconds(5),
                },
            ],
        };
        await Reject(() =>
            Task.FromResult(TradingCashBaselineAccounting.CashFlowAlreadyIncluded(collecting, concurrent))
        );
        Check(
            collecting.CashThb == 5096.69m && collecting.FundingThb == state.Live.FundingThb,
            "Late-visible flow during wallet collection is held without a second cash credit or guessed funding"
        );
        Check(
            TradingCashBaselineAccounting.CashFlowAlreadyIncluded(
                adopted.Live,
                concurrent with
                {
                    At = baseline.BrokerObservedAt.AddMinutes(-1),
                }
            )
                && !TradingCashBaselineAccounting.CashFlowAlreadyIncluded(
                    adopted.Live,
                    concurrent with
                    {
                        At = baseline.Cutoff.AddMinutes(1),
                    }
                ),
            "Older cash is absorbed; post-snapshot cash remains eligible for one normal funding event"
        );
        await Reject(() =>
            Task.FromResult(
                TradingCashBaselineAccounting.Adopt(state, baseline, account, baseline.Cutoff.AddSeconds(31))
            )
        );
        store.State = state with { Mode = TradingMode.AutoLive };
        await Reject(() => workflow.Preview(CancellationToken.None));
        await TradingCashPrecisionChecks.Run(adopted, account);
        await FundingEngineFlow(adopted, account);
        Console.WriteLine(
            "Cash baseline mocks passed: bound evidence, replacement/no funding reset, restart dedup, late-fill hold, revision race, stale/stopped guards and funding engine flow."
        );
    }

    private static async Task FundingEngineFlow(TradingState adopted, string accountKey)
    {
        var baseline = adopted.Live.CashBaselines.Single();
        var clock = new OrderCheckTime { Now = baseline.Cutoff.AddMinutes(1) };
        var rules = new TradingRuleSet
        {
            Version = "adaptive-intraday-v1",
            Symbols = [],
            Adaptive = new AdaptiveTradingPolicy(),
        };
        var rulesHash = TradingRuleHash.Compute(rules);
        var store = new FakeTradingStore(
            adopted with
            {
                Rules = rules,
                RulesHash = rulesHash,
                Mode = TradingMode.AutoLive,
                HeartbeatAt = clock.Now,
                Mandate = new TradingMandate(
                    Guid.NewGuid(),
                    accountKey,
                    rulesHash,
                    clock.Now.AddMinutes(-1),
                    clock.Now.AddHours(1),
                    null
                ),
                Live = adopted.Live with
                {
                    CashBaselines =
                    [
                        baseline with
                        {
                            Cutoff = baseline.BrokerObservedAt.AddSeconds(5),
                            RecordedAt = baseline.RecordedAt.AddSeconds(5),
                        },
                    ],
                },
            }
        );
        var initial = store.State;
        var flow = new CapitalCashFlow(
            "engine-collection-deposit",
            "THB",
            10m,
            0m,
            baseline.BrokerObservedAt.AddSeconds(1),
            CashFlowKind.Deposit,
            10m
        );
        var evidence = new FakeRiskEvidenceReader(
            new RiskAccountEvidence(
                clock.Now,
                [new Balance("THB", baseline.CashThb, baseline.CashThb)],
                [],
                [flow],
                clock.Now.AddDays(-90)
            )
        );
        var markets = new FakeTradingMarketReader([]) { Universe = new(clock.Now, [], 0, 0, null) };
        var exchange = new FakeTradingExchange { AvailableCash = baseline.CashThb, AvailableBase = 0m };
        var account = new AccountContext(accountKey);
        var logger = new FakeTradingLogger();
        using var gate = new SemaphoreSlim(1, 1);
        TradingEngine Restart()
        {
            var capital = new RiskCapitalReader(evidence, new FakeOrderStore(), store, markets, account, clock);
            var risk = new AutonomousRiskService(capital, new FakeRiskStore(), account, clock);
            return new TradingEngine(
                store,
                markets,
                exchange,
                account,
                gate,
                clock,
                autonomous: new AutonomousMarketMonitor(markets, risk, clock),
                adaptiveRisk: risk,
                logger: logger
            );
        }

        var engine = Restart();
        await engine.Tick(CancellationToken.None);
        await Restart().Tick(CancellationToken.None);
        Check(
            store.State.Fault == "Trading dependency or reconciliation failure — new entries paused"
                && store.State.Live.CashThb == initial.Live.CashThb
                && store.State.Live.FundingThb == initial.Live.FundingThb
                && store.State.ObservedFundingFlows.SequenceEqual(initial.ObservedFundingFlows)
                && logger.Messages.Count == 2
                && logger.Messages.All(message =>
                    message.Contains("MarketScan; type InvalidOperationException", StringComparison.Ordinal)
                )
                && exchange.Submissions == 0
                && exchange.Cancellations == 0,
            "Collection-interval funding faults through the engine and survives restart without accounting or orders"
        );

        evidence.Evidence = evidence.Evidence with
        {
            Balances = [new Balance("THB", baseline.CashThb + 10m, baseline.CashThb + 10m)],
            CashFlows =
            [
                flow with
                {
                    Id = "engine-before-wallet",
                    At = baseline.BrokerObservedAt.AddMinutes(-1),
                },
                flow with
                {
                    Id = "engine-after-wallet",
                    At = baseline.Cutoff.AddSeconds(6),
                },
            ],
        };
        engine = Restart();
        await engine.Tick(CancellationToken.None);
        await engine.Tick(CancellationToken.None);
        await Restart().Tick(CancellationToken.None);
        Check(
            store.State.Fault is null
                && store.State.Live.CashThb == initial.Live.CashThb + 10m
                && store.State.Live.FundingThb == initial.Live.FundingThb + 20m
                && store.State.ObservedFundingFlows.Length == initial.ObservedFundingFlows.Length + 2
                && store.State.Live.ExternalSales.SequenceEqual(initial.Live.ExternalSales)
                && store.State.Live.Fills.SequenceEqual(initial.Live.Fills)
                && logger.Messages.Count == 2
                && exchange.Submissions == 0
                && exchange.Cancellations == 0,
            "Engine absorbs old cash, credits later cash once and retains unknown sale profit across ticks/restart"
        );
    }

    private static async Task Reject<T>(Func<Task<T>> action)
    {
        try
        {
            _ = await action();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Expected cash-baseline rejection.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
