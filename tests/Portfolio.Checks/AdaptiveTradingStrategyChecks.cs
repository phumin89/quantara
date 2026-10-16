using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class AdaptiveTradingStrategyChecks
{
    public static Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        var rules = new TradingRuleSet();
        var bars = Enumerable
            .Range(0, 51)
            .Select(index => new TradingBar(
                now.AddSeconds(-300L * (51 - index)).ToUnixTimeSeconds(),
                100m,
                index == 50 ? 111m : 101m,
                99m,
                index == 50 ? 110m : 100m,
                index == 50 ? 2m : 1m
            ))
            .ToArray();
        var market = new TradingBook("BTC", now, 109.9m, 110m, 1m, 1m, bars);
        var breakout = AdaptiveTradingStrategy.Evaluate(rules, market, null, now);
        Check(breakout.Action == TradingAction.Buy, "adaptive strategy detects a completed breakout");
        Check(breakout.StrategyVersion == "breakout-v1", "breakout decision is versioned");
        Check(breakout.StopPrice is > 0m && breakout.StopPrice < market.Ask, "entry carries structural stop");
        var score = AdaptiveTradingStrategy.OpportunityScore(rules, market, breakout);
        Check(score > 0, "Eligible opportunities have a positive net reward to stop-risk score.");
        Check(
            AdaptiveTradingStrategy.OpportunityScore(rules, market, breakout with { StopPrice = 90m }) < score,
            "A wider structural stop ranks lower for the same net target."
        );
        Check(
            AdaptiveTradingStrategy.OpportunityScore(rules with { FeeBps = 50 }, market, breakout) < score,
            "Candidate ranking includes transaction costs."
        );

        var pullbackBars = bars.Select(
                (bar, index) =>
                    index == 50
                        ? bar with
                        {
                            Open = 99m,
                            High = 102m,
                            Low = 99m,
                            Close = 101m,
                            Volume = 2m,
                        }
                        : bar with
                        {
                            Open = 100m,
                            High = 101m,
                            Low = 99m,
                            Close = 100m,
                            Volume = 1m,
                        }
            )
            .ToArray();
        var pullback = AdaptiveTradingStrategy.Evaluate(
            rules,
            market with
            {
                Bars = pullbackBars,
                Bid = 101m,
                Ask = 101.1m,
            },
            null,
            now
        );
        Check(
            pullback.Action == TradingAction.Buy && pullback.EntryLimit is not null,
            "trend pullback creates a resting limit"
        );
        Check(pullback.StrategyVersion == "pullback-v1", "pullback decision is versioned");
        Check(
            pullback.StopPrice == 99m && pullback.StopPrice < pullback.EntryLimit,
            "pullback keeps the observed stop below entry"
        );

        var rangeBars = bars.Select(
                (bar, index) =>
                {
                    var close = index % 2 == 0 ? 99m : 101m;
                    return index == 50
                        ? bar with
                        {
                            Open = 99m,
                            High = 102m,
                            Low = 98m,
                            Close = 101m,
                            Volume = 1m,
                        }
                        : bar with
                        {
                            Open = close,
                            High = close + 1m,
                            Low = close - 1m,
                            Close = close,
                            Volume = 1m,
                        };
                }
            )
            .ToArray();
        var range = AdaptiveTradingStrategy.Evaluate(
            rules,
            market with
            {
                Bars = rangeBars,
                Bid = 101m,
                Ask = 101.1m,
            },
            null,
            now
        );
        Check(
            range.Action == TradingAction.Buy && range.EntryLimit is not null,
            "range rebound creates a resting limit"
        );
        Check(range.StrategyVersion == "range-v1", "range decision is versioned");

        var holding = new TradingHolding("BTC", 1m, 100m, now.AddMinutes(-1), 100m);
        var exit = AdaptiveTradingStrategy.Evaluate(rules, market with { Bid = 98m, Ask = 98.1m }, holding, now);
        Check(exit.Action == TradingAction.Sell, "adaptive strategy keeps deterministic exits");

        var stale = AdaptiveTradingStrategy.Evaluate(
            rules,
            market with
            {
                ObservedAt = now.AddMinutes(-10),
            },
            null,
            now
        );
        Check(stale.Action == TradingAction.Wait, "stale quotes never create entries");

        var future = AdaptiveTradingStrategy.Evaluate(
            rules,
            market with
            {
                Bars = bars[..^1].Append(bars[^1] with { Timestamp = now.AddMinutes(5).ToUnixTimeSeconds() }).ToArray(),
            },
            null,
            now
        );
        Check(future.Action == TradingAction.Wait, "future candles never create entries");

        var malformed = bars.ToArray();
        malformed[30] = malformed[30] with { Timestamp = malformed[30].Timestamp + 1 };
        Check(
            AdaptiveTradingStrategy.Evaluate(rules, market with { Bars = malformed }, null, now).Action
                == TradingAction.Wait,
            "gapped candles never create entries"
        );
        var zeroVolume = bars.ToArray();
        zeroVolume[10] = zeroVolume[10] with { Volume = 0m };
        Check(
            AdaptiveTradingStrategy.Evaluate(rules, market with { Bars = zeroVolume }, null, now).Action
                == TradingAction.Wait,
            "zero volume never creates entries"
        );
        Check(
            AdaptiveTradingStrategy.Evaluate(rules, market with { Bid = 0m }, null, now).Action == TradingAction.Wait,
            "invalid quote never creates entries"
        );

        Console.WriteLine("Adaptive trading strategy checks passed.");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
