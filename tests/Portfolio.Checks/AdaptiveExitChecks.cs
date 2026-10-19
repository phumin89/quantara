using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class AdaptiveExitChecks
{
    public static async Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
        var rules = new TradingRuleSet
        {
            Version = "adaptive-intraday-v3",
            Symbols = [],
            Adaptive = new(),
            ExitPolicy = new(),
        };
        rules.Validate();
        var book = Book(now, 103m);
        var entry = new TradingDecision(
            TradingAction.Buy,
            "Trend entry",
            book.Bars[^1].Timestamp,
            100m,
            StopPrice: 90m
        );
        var seed = AdaptiveExitStrategy.Create(rules, book, entry, Guid.NewGuid(), new string('a', 64), now);
        var plan = AdaptiveExitStrategy.Anchor(seed, now.AddHours(-2));
        var holding = new TradingHolding("BTC", 1, 100, now.AddHours(-2), 103) { StopPrice = 90, ExitPlan = plan };

        var review = AdaptiveExitStrategy.Advance(rules, book, holding, now);
        Check(
            review.Action == TradingAction.Wait && review.Plan.ReviewCount == 1,
            "A healthy trend is reviewed instead of forcibly sold at 120 minutes."
        );
        Check(
            review.Plan.NextReviewAt > now && review.Plan.HardExitAt == plan.HardExitAt,
            "A review never extends the original hard holding deadline."
        );
        var repeated = AdaptiveExitStrategy.Advance(
            rules,
            book with
            {
                ObservedAt = now.AddSeconds(1),
            },
            holding with
            {
                ExitPlan = review.Plan,
            },
            now.AddSeconds(1)
        );
        Check(
            repeated.Plan.ReviewCount == 1 && repeated.Plan.NextReviewAt == review.Plan.NextReviewAt,
            "Repeated quote events do not create repeated reviews."
        );
        Check(repeated.Plan.LastReason == review.Plan.LastReason, "Quote refresh preserves the last review reason.");
        var firstRange = AdaptiveExitStrategy.Advance(
            rules,
            Book(now, 100.5m, true),
            holding with
            {
                CostThb = 100,
                PeakBid = 100.5m,
                ExitPlan = plan with { TargetPrice = 120 },
            },
            now
        );
        Check(
            firstRange.Plan.Regime == TradingExitRegime.Trend && firstRange.Plan.RegimeEvidenceCount == 1,
            "One completed range review cannot flip the entry regime."
        );
        var secondAt = firstRange.Plan.NextReviewAt;
        var secondRange = AdaptiveExitStrategy.Advance(
            rules,
            Book(secondAt, 100.5m, true),
            holding with
            {
                PeakBid = 100.5m,
                ExitPlan = firstRange.Plan,
            },
            secondAt
        );
        Check(
            secondRange.Plan.Regime == TradingExitRegime.Range
                && secondRange.Plan.HardExitAt <= plan.HardExitAt
                && secondRange.Plan.EffectiveStop >= plan.EffectiveStop,
            "Two distinct reviews may change regime without extending time or loosening protection."
        );

        var empty = book with { Bars = [], Bid = 89, Ask = 89.01m };
        var stopped = AdaptiveExitStrategy.Advance(rules, empty, holding, now);
        Check(
            stopped.Action == TradingAction.Sell && stopped.Reason.Contains("Protective", StringComparison.Ordinal),
            "A fresh protective stop works even while candle history is unavailable."
        );
        var deadline = plan.HardExitAt;
        var expired = AdaptiveExitStrategy.Advance(
            rules,
            empty with
            {
                Bid = 103,
                Ask = 103.01m,
                ObservedAt = deadline,
            },
            holding,
            deadline
        );
        Check(
            expired.Action == TradingAction.Sell
                && expired.Reason.Contains("Maximum holding", StringComparison.Ordinal),
            "The anchored hard deadline remains enforceable without review candles."
        );
        var stale = AdaptiveExitStrategy.Advance(rules, empty with { ObservedAt = now.AddMinutes(-10) }, holding, now);
        Check(
            stale.Action == TradingAction.Wait && stale.Plan.HardExitAt == plan.HardExitAt,
            "Stale quotes neither dispatch an exit nor reset its deadline."
        );

        var target = AdaptiveExitStrategy.Advance(
            rules,
            Book(now, 115),
            holding with
            {
                PeakBid = 115,
                ExitPlan = plan with { TargetPrice = 110 },
            },
            now
        );
        Check(
            target.Action == TradingAction.Wait && target.Plan.TrailingActive,
            "A trend target activates protection rather than automatically selling the full position."
        );
        var range = AdaptiveExitStrategy.Advance(
            rules,
            Book(now, 103, true),
            holding with
            {
                ExitPlan = plan with { Regime = TradingExitRegime.Range, TargetPrice = 102 },
            },
            now
        );
        Check(
            range.Action == TradingAction.Sell && range.Reason.Contains("Range target", StringComparison.Ordinal),
            "A range target remains a deterministic exit."
        );

        var smallGain = AdaptiveExitStrategy.Advance(
            rules,
            Book(now, 100.1m),
            holding with
            {
                PeakBid = 100.1m,
                ExitPlan = plan with { NextReviewAt = now.AddMinutes(20) },
            },
            now
        );
        Check(
            !smallGain.Plan.TrailingActive,
            "Gross gains below fees, slippage and volatility buffer do not arm trailing."
        );
        var spikeBook = Book(now, 105) with
        {
            Bars = book.Bars.Select(bar => bar with { High = bar.High + 20, Low = bar.Low - 20 }).ToArray(),
        };
        var spike = AdaptiveExitStrategy.Advance(
            rules,
            spikeBook,
            holding with
            {
                PeakBid = 110,
                ExitPlan = plan with { TrailingActive = true, EffectiveStop = 101, NextReviewAt = now.AddMinutes(20) },
            },
            now
        );
        Check(
            spike.Plan.EffectiveStop >= 101 && spike.Plan.TrailingActive,
            "An ATR expansion cannot loosen an armed stop or disarm trailing."
        );
        Check(
            AdaptiveExitStrategy.Anchor(plan, now.AddMinutes(5)) == plan,
            "Later partial fills and restart cannot re-anchor a holding deadline."
        );

        var legacy = rules with { Version = "adaptive-intraday-v2", ExitPolicy = null };
        var legacyExit = AdaptiveTradingStrategy.Evaluate(legacy, book, holding with { ExitPlan = null }, now);
        Check(
            legacyExit.Action == TradingAction.Sell && legacyExit.Reason == "Time exit",
            "Legacy v2 retains its exact original timed-exit behavior."
        );
        await EnginePlanChecks();
        await PartialFillPlanChecks();
    }

    private static async Task PartialFillPlanChecks()
    {
        using var fixture = new AutonomousFixture();
        var rules = fixture.Rules with { Version = "adaptive-intraday-v3", ExitPolicy = new() };
        fixture.Store.State = fixture.Store.State with { Rules = rules, RulesHash = TradingRuleHash.Compute(rules) };
        var control = new TradingControl(
            fixture.Store,
            fixture.Exchange,
            fixture.Markets,
            fixture.Account,
            new("fixture-only") { AdaptiveExitLiveAvailable = true },
            fixture.Gate,
            fixture.Time,
            adaptiveRisk: fixture.Risk
        );
        await control.Activate(
            fixture.Account.AccountKey,
            fixture.Store.State.RulesHash,
            fixture.Time.Now.AddHours(1),
            "enable-auto-trading",
            CancellationToken.None
        );
        await fixture.Engine.Tick(CancellationToken.None);
        var order = fixture.Store.State.Live.Orders.Single();
        var first = new TradingFill(
            Guid.NewGuid(),
            order.Id,
            "partial-one",
            "ETH",
            TradeSide.Buy,
            1,
            110,
            110,
            .28m,
            fixture.Time.Now
        );
        fixture.Exchange.Observation = intent => new("partial", intent.BrokerOrderId, intent.Amount - 110.28m, [first]);
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        fixture.Markets.Books = [fixture.Book("ETH")];
        await fixture.Engine.Tick(CancellationToken.None);
        var initial =
            fixture.Store.State.Live.Positions.Single().ExitPlan
            ?? throw new InvalidOperationException("First partial fill must install its plan.");
        var second = first with
        {
            Id = Guid.NewGuid(),
            BrokerFillId = "partial-two",
            Quantity = .5m,
            GrossThb = 55,
            FeeThb = .14m,
            OccurredAt = fixture.Time.Now,
        };
        fixture.Exchange.Observation = intent =>
            new("partial", intent.BrokerOrderId, intent.Amount - 165.42m, [first, second]);
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        fixture.Markets.Books = [fixture.Book("ETH")];
        await fixture.Restart().Tick(CancellationToken.None);
        var position = fixture.Store.State.Live.Positions.Single();
        Check(
            position.Quantity == 1.5m
                && position.CostThb == 165.42m
                && position.ExitPlan is { } continued
                && continued.EnteredAt == initial.EnteredAt
                && continued.HardExitAt == initial.HardExitAt,
            "Partial fills and restart preserve exact quantity/cost and the first-fill holding deadline."
        );
        var authority = fixture.Store.State.Mandate;
        var rejected = false;
        try
        {
            await control.Configure(
                rules with
                {
                    Version = "adaptive-intraday-v2",
                    ExitPolicy = null,
                },
                CancellationToken.None
            );
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Check(
            rejected && fixture.Store.State.Mandate == authority,
            "Material policy changes cannot orphan an owned adaptive position or rewrite its mandate."
        );
    }

    private static async Task EnginePlanChecks()
    {
        using var fixture = new AutonomousFixture();
        var rules = fixture.Rules with { Version = "adaptive-intraday-v3", ExitPolicy = new() };
        fixture.Store.State = fixture.Store.State with
        {
            Rules = rules,
            RulesHash = TradingRuleHash.Compute(rules),
            Mode = TradingMode.Paper,
        };
        await fixture.Engine.Tick(CancellationToken.None);
        var order = fixture.Store.State.Paper.Orders.Single();
        Check(order.ExitPlan is { Anchored: false }, "A v3 intent commits an exit plan before its simulated fill.");
        fixture.Time.Now = fixture.Time.Now.AddSeconds(1);
        fixture.Markets.Books = [fixture.Book("ETH")];
        await fixture.Engine.Tick(CancellationToken.None);
        var position = fixture.Store.State.Paper.Positions.Single();
        Check(
            position.ExitPlan is { Anchored: true } installed
                && installed.EnteredAt == position.OpenedAt
                && installed.EntryIntentId == order.Id,
            "Confirmed paper fills install and anchor the original entry plan."
        );
        Check(fixture.Exchange.Submissions == 0, "Paper validation never calls the real execution adapter.");
        var blocked = false;
        try
        {
            await fixture.Activate();
        }
        catch (ArgumentException)
        {
            blocked = true;
        }

        Check(
            blocked && fixture.Store.State.Mode == TradingMode.Paper,
            "Unvalidated adaptive exits cannot activate live authority."
        );
    }

    private static TradingBook Book(DateTimeOffset now, decimal bid, bool range = false)
    {
        var end = now.ToUnixTimeSeconds() / 300 * 300;
        var bars = Enumerable
            .Range(0, 51)
            .Select(index =>
            {
                var close = range ? 100m : 90m + index * 0.25m;
                return new TradingBar(end - (51 - index) * 300, close, close + 1, close - 1, close, 1);
            })
            .ToArray();
        return new("BTC", now, bid, bid + 0.01m, 10, 10, bars);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        Console.WriteLine("Passed: " + message);
    }
}
