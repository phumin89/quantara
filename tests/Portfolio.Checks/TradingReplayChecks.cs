using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingReplayChecks
{
    public static async Task Run()
    {
        using var fixture = new AutonomousFixture();
        var rules = fixture.Rules with { Version = "adaptive-intraday-v2", TimeExitMinutes = 5 };
        var start = fixture.Time.Now.ToUnixTimeSeconds() / 300 * 300 - 300 * 160;
        var bars = Enumerable
            .Range(0, 160)
            .Select(index => new TradingBar(start + index * 300, 100, 101, 99, 100, 1))
            .ToArray();
        foreach (var signal in new[] { 50, 120 })
        {
            bars[signal] = new(start + signal * 300, 100, 111, 99, 110, 2);
            bars[signal + 1] = new(start + (signal + 1) * 300, 110, 111, 109, 110, 1);
            bars[signal + 2] = new(start + (signal + 2) * 300, 110, 114.1m, 110, 114, 1);
            bars[signal + 3] = new(start + (signal + 3) * 300, 114, 114.2m, 113.9m, 114, 1);
        }

        var market = new TradingBook("ETH", fixture.Time.Now, 100m, 101m, 10m, 10m, []);
        var result = TradingReplayEvaluator.Evaluate(rules, "ETH", bars, 300, 1000, market);
        var training = result.Single(item => item.Strategy == "breakout" && item.Segment == "training");
        var validation = result.Single(item => item.Strategy == "breakout" && item.Segment == "validation");
        Check(
            training.Trades == 1
                && validation.Trades == 1
                && training.FeesThb > 4.9m
                && training.NetThb is > 0 and < 30
                && training.MaxDrawdownThb is > 0,
            "Replay must compute later-bar fills, two-sided fees, net and marked drawdown on separate segments."
        );
        Check(
            result.All(item => item.InsufficientSamples && item.CashBenchmarkThb == 0),
            "Small samples never claim a proven edge over cash."
        );
        var cheapBars = bars.Select(bar =>
                bar with
                {
                    Open = bar.Open / 100000m,
                    High = bar.High / 100000m,
                    Low = bar.Low / 100000m,
                    Close = bar.Close / 100000m,
                }
            )
            .ToArray();
        var cheap = TradingReplayEvaluator
            .Evaluate(rules, "ETH", cheapBars, 300, 1000, market with { PriceStep = 0.0000001m, PriceScale = 7 })
            .Single(item => item.Strategy == "breakout" && item.Segment == "training");
        Check(
            cheap.Trades == training.Trades && cheap.NetThb is > 0 and < 30,
            "Low-price markets retain valid historical signals and exits at their verified precision."
        );
        var changedFuture = bars.Select(
                (bar, index) =>
                    index >= 112 ? bar with { Close = 100, Open = 100, High = 101, Low = 99, Volume = 1 } : bar
            )
            .ToArray();
        Check(
            TradingReplayEvaluator
                .Evaluate(rules, "ETH", changedFuture, 300, 1000, market)
                .Single(item => item.Strategy == "breakout" && item.Segment == "training") == training,
            "Validation candles must not influence training results."
        );
        var ambiguous = bars.ToArray();
        var touched = bars.ToArray();
        const decimal limit = 110.77m;
        touched[51] = new(touched[51].Timestamp, 111, 111, limit / 1.005m, 111, 1);
        // Remaining candles stay above the resting buy until its expiry.
        for (var index = 52; index < 56; index++)
        {
            touched[index] = new(touched[index].Timestamp, 114, 115, 113, 114, 1);
        }

        Check(
            TradingReplayEvaluator
                .Evaluate(rules, "ETH", touched, 300, 1000, market)
                .Single(item => item.Strategy == "breakout" && item.Segment == "training")
                .Trades == 0,
            "A candle that only touches the modeled ask limit cannot establish a filled trade."
        );
        ambiguous[51] = ambiguous[51] with { Low = 98 };
        var uncertain = TradingReplayEvaluator
            .Evaluate(rules, "ETH", ambiguous, 300, 1000, market)
            .Single(item => item.Strategy == "breakout" && item.Segment == "training");
        Check(
            uncertain.UnknownFills > 0 && uncertain.NetThb is null && uncertain.MaxDrawdownThb is null,
            "Ambiguous same-bar entry/exit never becomes profitable invented execution."
        );
        var gap = bars.Where((_, index) => index != 51).ToArray();
        Check(
            TradingReplayEvaluator
                .Evaluate(rules, "ETH", gap, 300, 1000, market)
                .Single(item => item.Strategy == "breakout" && item.Segment == "training")
                .NetThb
                is null,
            "Missing market history cannot establish zero trades or zero profit."
        );
        var empty = TradingReplayEvaluator.Evaluate(rules, "ETH", [], 900, 1000, market);
        Check(empty.All(item => item.NetThb is null && item.InsufficientSamples), "Empty data stays Unknown.");

        var history = new TradingHistoryStub();
        var markets = new FakeTradingMarketReader([market]);
        var service = new TradingEvaluationService(history, fixture.Time, markets);
        var evaluation = await service.Evaluate(new(["ETH"], Rules: rules), CancellationToken.None);
        Check(
            history.Calls == 3
                && evaluation.Metrics.Count == 18
                && !evaluation.VerifiedProfit
                && evaluation.Protocol == "ohlc-scenario-v3"
                && evaluation.Warnings.Any(warning =>
                    warning.Contains("historical rule changes are unverified", StringComparison.Ordinal)
                )
                && evaluation.Warnings.Any(warning =>
                    warning.Contains("price/base/quote scales 2/8/2", StringComparison.Ordinal)
                )
                && evaluation.RulesHash == TradingRuleHash.Compute(rules),
            "The study covers three native frames with frozen rules."
        );
        history.Unavailable = true;
        Check(
            (await service.Evaluate(new(["ETH"], Rules: rules), CancellationToken.None)).Metrics.All(item =>
                item.NetThb is null
            ),
            "A failed history source never yields fake zero profit."
        );
        history.Unavailable = false;
        foreach (
            var invalid in new TradingBook?[]
            {
                null,
                market with
                {
                    PriceStep = 0m,
                },
                market with
                {
                    BaseScale = 19,
                },
                market with
                {
                    PriceStep = 0.001m,
                },
                market with
                {
                    ObservedAt = fixture.Time.Now.AddMinutes(-10),
                },
                market with
                {
                    ObservedAt = fixture.Time.Now.AddSeconds(1),
                },
            }
        )
        {
            markets.Books = invalid is null ? [] : [invalid];
            var missingPrecision = await service.Evaluate(new(["ETH"], Rules: rules), CancellationToken.None);
            Check(
                missingPrecision.Metrics.All(metric => metric.NetThb is null && metric.InsufficientSamples)
                    && missingPrecision.Warnings.Any(warning =>
                        warning.Contains("precision unavailable", StringComparison.Ordinal)
                    ),
                "Missing, invalid, stale or future precision stays Unknown rather than inventing a zero-trade result."
            );
        }

        markets.Books = [market];
        var coarse = TradingReplayEvaluator
            .Evaluate(rules, "ETH", bars, 300, 1000, market with { BaseScale = 0 })
            .Single(item => item.Strategy == "breakout" && item.Segment == "training");
        // Nine units: entry 996.93 + 2.50 fee; exit 1024.02 - 2.57 fee.
        Check(
            coarse.Trades == 1 && coarse.NetThb == 22.02m && coarse.FeesThb == 5.07m,
            "Replay uses whole base units and fees on the filled notional, not the unused reference budget."
        );
        var coarseTick = TradingReplayEvaluator
            .Evaluate(rules, "ETH", bars, 300, 1000, market with { PriceStep = 1m, BaseScale = 0 })
            .Single(item => item.Strategy == "breakout" && item.Segment == "training");
        Check(
            coarseTick.Trades == 1 && coarseTick.NetThb == 30.95m && coarseTick.FeesThb == 5.05m,
            "A whole-tick sell uses 114, above the 113.772 slippage floor; nine units cost 992.48 including fees."
        );
        fixture.Store.State = fixture.Store.State with
        {
            Rules = rules,
            RulesHash = TradingRuleHash.Compute(rules),
            Autonomy = new(
                fixture.Time.Now,
                fixture.Time.Now,
                1,
                1,
                [new("ETH", fixture.Time.Now, "No signal", null, null)],
                null,
                "Researching",
                fixture.Time.Now
            ),
            Evaluation = evaluation with { Protocol = "ohlc-scenario-v1" },
        };
        using var worker = new TradingEvaluationWorker(
            fixture.Store,
            service,
            fixture.Gate,
            fixture.Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TradingEvaluationWorker>.Instance
        );
        foreach (var protocol in new[] { "ohlc-scenario-v1", "ohlc-scenario-v2" })
        {
            fixture.Store.State = fixture.Store.State with { Evaluation = evaluation with { Protocol = protocol } };
            var before = fixture.Store.State;
            var priorCalls = history.Calls;
            await worker.Review(CancellationToken.None);
            var calls = history.Calls;
            await worker.Review(CancellationToken.None);
            Check(
                fixture.Store.State.Evaluation is not null
                    && fixture.Store.State.Evaluation.Protocol == TradingReplayEvaluator.Protocol
                    && calls == priorCalls + 3
                    && history.Calls == calls
                    && fixture.Store.State.Mode == before.Mode
                    && fixture.Store.State.Mandate == before.Mandate
                    && fixture.Exchange.Submissions == 0,
                "Each retained replay protocol refreshes once without changing authority or submitting orders."
            );
        }

        Console.WriteLine(
            "Historical replay, chronology, costs, Unknown outcomes and daily read-only study checks passed."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
