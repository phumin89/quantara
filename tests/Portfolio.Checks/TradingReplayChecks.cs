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

        var result = TradingReplayEvaluator.Evaluate(rules, "ETH", bars, 300, 1000);
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
        var changedFuture = bars.Select(
                (bar, index) =>
                    index >= 112 ? bar with { Close = 100, Open = 100, High = 101, Low = 99, Volume = 1 } : bar
            )
            .ToArray();
        Check(
            TradingReplayEvaluator
                .Evaluate(rules, "ETH", changedFuture, 300, 1000)
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
                .Evaluate(rules, "ETH", touched, 300, 1000)
                .Single(item => item.Strategy == "breakout" && item.Segment == "training")
                .Trades == 0,
            "A candle that only touches the modeled ask limit cannot establish a filled trade."
        );
        ambiguous[51] = ambiguous[51] with { Low = 98 };
        var uncertain = TradingReplayEvaluator
            .Evaluate(rules, "ETH", ambiguous, 300, 1000)
            .Single(item => item.Strategy == "breakout" && item.Segment == "training");
        Check(
            uncertain.UnknownFills > 0 && uncertain.NetThb is null && uncertain.MaxDrawdownThb is null,
            "Ambiguous same-bar entry/exit never becomes profitable invented execution."
        );
        var gap = bars.Where((_, index) => index != 51).ToArray();
        Check(
            TradingReplayEvaluator
                .Evaluate(rules, "ETH", gap, 300, 1000)
                .Single(item => item.Strategy == "breakout" && item.Segment == "training")
                .NetThb
                is null,
            "Missing market history cannot establish zero trades or zero profit."
        );
        var empty = TradingReplayEvaluator.Evaluate(rules, "ETH", [], 900, 1000);
        Check(empty.All(item => item.NetThb is null && item.InsufficientSamples), "Empty data stays Unknown.");

        var history = new TradingHistoryStub();
        var service = new TradingEvaluationService(history, fixture.Time);
        var evaluation = await service.Evaluate(new(["ETH"], Rules: rules), CancellationToken.None);
        Check(
            history.Calls == 3
                && evaluation.Metrics.Count == 18
                && !evaluation.VerifiedProfit
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
        };
        using var worker = new TradingEvaluationWorker(
            fixture.Store,
            service,
            fixture.Gate,
            fixture.Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TradingEvaluationWorker>.Instance
        );
        var before = fixture.Store.State;
        await worker.Review(CancellationToken.None);
        var calls = history.Calls;
        await worker.Review(CancellationToken.None);
        Check(
            fixture.Store.State.Evaluation is not null
                && history.Calls == calls
                && fixture.Store.State.Mode == before.Mode
                && fixture.Store.State.Mandate == before.Mandate
                && fixture.Exchange.Submissions == 0,
            "Daily study persists once without changing authority or submitting orders."
        );
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
