using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingReconciliationStopChecks
{
    public static async Task Run()
    {
        await Reconcile(installedPlan: true);
        await Reconcile(installedPlan: false);
        Console.WriteLine(
            "Reconciliation preserves installed stops and initializes first-fill protection without broker sends."
        );
    }

    private static async Task Reconcile(bool installedPlan)
    {
        using var fixture = new AutonomousFixture();
        var preparedAt = fixture.Time.Now;
        var fillAt = preparedAt.AddSeconds(1);
        var rules = fixture.Rules with
        {
            Version = installedPlan ? "adaptive-intraday-v3" : "adaptive-intraday-v2",
            ExitPolicy = installedPlan ? new AdaptiveExitPolicy() : null,
        };
        rules.Validate();
        var hash = TradingRuleHash.Compute(rules);
        var id = Guid.NewGuid();
        var book = fixture.Book("ETH");
        var signal = new RiskSignal(99m, book.Bars[^2].Timestamp, preparedAt, "Synthetic reconciliation protection");
        TradingExitPlan? seed = null;
        TradingExitPlan? plan = null;
        if (installedPlan)
        {
            seed = AdaptiveExitStrategy.Create(
                rules,
                book,
                new TradingDecision(
                    TradingAction.Buy,
                    "Synthetic entry",
                    book.Bars[^1].Timestamp,
                    book.Ask,
                    EntryLimit: 110m,
                    StopPrice: signal.StopPrice,
                    StopCandle: signal.StopCandle
                ),
                id,
                hash,
                preparedAt
            );
            plan = AdaptiveExitStrategy.Anchor(seed, fillAt) with
            {
                EffectiveStop = 100m,
                TrailingActive = true,
                ReviewCount = 1,
                Revision = seed.Revision + 1,
                LastReason = "Synthetic tightened protection",
            };
            plan.Validate();
        }

        var market = new RiskMarketEvidence(
            book.Symbol,
            preparedAt,
            book.Bid,
            book.Ask,
            10000m,
            100m,
            seed?.EntryAtr ?? 1m,
            book.MinimumNotional,
            book.PriceStep,
            book.PriceScale,
            book.QuoteScale,
            true,
            true
        );
        var policy = rules.Adaptive?.Risk ?? throw new InvalidOperationException("Synthetic adaptive policy missing.");
        var capital = new CapitalSnapshot(
            fixture.Account.AccountKey,
            preparedAt,
            1000m,
            1000m,
            0m,
            0m,
            [],
            false,
            null
        );
        var decision = AdaptiveRiskEngine.Evaluate(policy, capital, market, TradeSide.Buy, 110m, signal, preparedAt);
        Check(
            decision.Accepted && decision.Amount >= 100m,
            "The retained fixture sizing must authorize its exact amount."
        );
        var sizing = new RiskSizingSnapshot(
            Guid.NewGuid(),
            fixture.Account.AccountKey,
            hash,
            policy,
            capital,
            signal,
            market,
            decision,
            0m
        );
        var fill = new TradingFill(
            Guid.NewGuid(),
            id,
            "synthetic-buy-fill",
            "ETH",
            TradeSide.Buy,
            50m / 110m,
            110m,
            50m,
            .1m,
            fillAt
        );
        var intent = new TradingIntent(
            id,
            "ETH",
            TradeSide.Buy,
            100m,
            110m,
            installedPlan ? 49.9m : 100m,
            preparedAt,
            book.Bars[^1].Timestamp,
            hash,
            "Synthetic retained order",
            installedPlan ? "partial" : "submitted",
            "synthetic-broker-order",
            LastInspectedAt: preparedAt.AddSeconds(-1),
            Sizing: sizing
        )
        {
            ExitPlan = seed,
        };
        var ledger = fixture.Store.State.Live with { FundingThb = 1000m, CashThb = 1000m, OpenedAt = preparedAt };
        if (installedPlan)
        {
            ledger = TradingLedgerAccounting.Apply(ledger, fill);
            ledger = ledger with
            {
                Positions =
                [
                    ledger.Positions.Single() with
                    {
                        StopPrice = 100m,
                        TargetPrice = 110m + decimal.Max(market.Atr * 2, 110m * rules.TakeProfitBps / 10000m),
                        TrailingDistance = decimal.Max(market.Atr, 110m * rules.TrailingStopBps / 10000m),
                        ExitPlan = plan,
                    },
                ],
            };
        }

        fixture.Store.State = fixture.Store.State with
        {
            Rules = rules,
            RulesHash = hash,
            Mode = TradingMode.Stopped,
            Live = ledger with { Orders = [intent] },
        };
        fixture.Exchange.Observation = _ => new("partial", intent.BrokerOrderId, 49.9m, [fill]);
        TradingLedger? beforeMarketFailure = null;
        var marketReads = 0;
        fixture.Markets.BeforeRead = () =>
        {
            marketReads++;
            beforeMarketFailure ??= fixture.Store.State.Live;
            throw new HttpRequestException("Synthetic market read failure after reconciliation.");
        };
        fixture.Time.Now = fillAt.AddSeconds(31);
        await fixture.Engine.Tick(CancellationToken.None);

        var reconciled =
            beforeMarketFailure
            ?? throw new InvalidOperationException("The market failure must follow reconciliation.");
        Check(
            marketReads > 0 && reconciled.Orders.Single().LastInspectedAt == fixture.Time.Now,
            "Broker observation must be persisted before the market read fails."
        );
        var retained = fixture.Store.State.Live;
        var holding = retained.Positions.Single();
        Check(
            holding.StopPrice == (installedPlan ? 100m : 99m) && holding.ExitPlan == plan,
            "A duplicate buy fill must preserve its installed effective stop; a first fill without a plan initializes the original sizing stop."
        );
        Check(
            retained.Fills.Count == 1
                && retained.Fills.Single() == fill
                && retained.CashThb == 949.9m
                && holding.CostThb == 50.1m
                && holding.Quantity == fill.Quantity,
            "Cumulative fill history must retain one exact fill and one cash/cost movement."
        );
        if (installedPlan)
        {
            Check(
                holding.TargetPrice == ledger.Positions.Single().TargetPrice
                    && holding.TrailingDistance == ledger.Positions.Single().TrailingDistance
                    && retained.CashThb == ledger.CashThb
                    && retained.Fills.SequenceEqual(ledger.Fills),
                "Duplicate reconciliation cannot change the installed plan, target, distance or fill accounting."
            );
        }

        Check(
            fixture.Store.State.Mode == TradingMode.Stopped
                && fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0,
            "Stopped reconciliation and market failure never authorize a submission or cancellation."
        );
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
