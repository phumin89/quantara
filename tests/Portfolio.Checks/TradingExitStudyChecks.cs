using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingExitStudyChecks
{
    public static Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        var state = TradingStateFactory.Create(now);
        var unchanged = TradingExitStudy.Advance(state, [], now);
        if (!ReferenceEquals(state, unchanged))
        {
            throw new InvalidOperationException("Exit study must skip when no seeded plans exist.");
        }

        var terminal = new TradingExitStudySample
        {
            EntryIntentId = Guid.NewGuid(),
            Symbol = "BTC",
            CreatedAt = now.AddHours(-1),
            Status = "hypothetical",
            HypotheticalNetThb = 12.5m,
            HypotheticalExitAt = now.AddMinutes(-5),
            Quantity = 1m,
        };
        var terminalState = state with { ExitStudies = [terminal] };
        var preserved = TradingExitStudy.Advance(terminalState, [], now);
        if (preserved.ExitStudies.Single() != terminal)
        {
            throw new InvalidOperationException(
                "Terminal hypothetical outcome must remain immutable without new evidence."
            );
        }

        var seededNow = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
        var rules = new TradingRuleSet
        {
            Version = "adaptive-intraday-v3",
            Symbols = [],
            Adaptive = new(),
            ExitPolicy = new(),
        };
        var entryBook = Book(seededNow, 110m);
        var entryId = Guid.NewGuid();
        var decision = new TradingDecision(
            TradingAction.Buy,
            "fixture",
            entryBook.Bars[^1].Timestamp,
            100m,
            100m,
            90m,
            entryBook.Bars[^1].Timestamp
        );
        var seed = AdaptiveExitStrategy.Create(rules, entryBook, decision, entryId, new string('a', 64), seededNow);
        var order = new TradingIntent(
            entryId,
            "BTC",
            TradeSide.Buy,
            1m,
            100m,
            0m,
            seededNow,
            decision.Candle,
            new string('a', 64),
            "fixture",
            "filled"
        )
        {
            ExitStudyPlan = seed,
        };
        var fill = new TradingFill(
            Guid.NewGuid(),
            entryId,
            "study-buy",
            "BTC",
            TradeSide.Buy,
            1m,
            100m,
            100m,
            1m,
            seededNow
        );
        var paper = state.Paper with { Orders = [order], Fills = [fill] };
        var studied = TradingExitStudy.Advance(
            state with
            {
                Rules = rules,
                Paper = paper,
            },
            [entryBook],
            seededNow.AddMinutes(1)
        );
        var sample = studied.ExitStudies.Single(item => item.SourceMode == "paper");
        if (sample.Quantity != 1m || sample.EntryCostThb != 101m)
        {
            throw new InvalidOperationException(
                "Seeded confirmed fill must use actual fee-inclusive quantity and cost."
            );
        }

        if (studied.Paper != paper || studied.Mode != state.Mode || studied.Mandate != state.Mandate)
        {
            throw new InvalidOperationException(
                "Read-only exit study cannot mutate orders, fills or execution authority."
            );
        }

        var stopAt = seededNow.AddMinutes(1);
        var stopBook = entryBook with { Bid = 89, Ask = 89.01m, ObservedAt = stopAt, Bars = [] };
        var stopped = TradingExitStudy.Advance(studied, [stopBook], stopAt);
        var stoppedSample = stopped.ExitStudies.Single();
        var gross = 89m * (1 - seed.SlippageBps / 10000m);
        var expected = gross - decimal.Ceiling(gross * seed.FeeBps / 10000m * 100) / 100 - 101;
        if (stoppedSample.Status != "hypothetical" || stoppedSample.HypotheticalNetThb != expected)
        {
            throw new InvalidOperationException(
                "Hypothetical exit must subtract slippage, one exit fee and actual entry cost exactly once."
            );
        }

        var later = TradingExitStudy.Advance(
            stopped,
            [stopBook with { Bid = 120, ObservedAt = stopAt.AddMinutes(1) }],
            stopAt.AddMinutes(1)
        );
        if (later.ExitStudies.Single() != stoppedSample)
        {
            throw new InvalidOperationException("Later prices cannot rewrite a completed hypothetical outcome.");
        }

        var sell = fill with
        {
            Id = Guid.NewGuid(),
            IntentId = Guid.NewGuid(),
            Side = TradeSide.Sell,
            GrossThb = 105,
            FeeThb = 1,
            OccurredAt = stopAt.AddSeconds(10),
            RealizedNetThb = 3,
        };
        var paired = TradingExitStudy.Advance(
            stopped with
            {
                Paper = paper with { Fills = [fill, sell] },
            },
            [],
            stopAt.AddMinutes(1)
        );
        if (
            paired.ExitStudies.Single().BaselineNetThb != 3
            || paired.ExitStudies.Single().HypotheticalNetThb != expected
        )
        {
            throw new InvalidOperationException(
                "Baseline uses confirmed realized net without overwriting hypothetical net."
            );
        }

        var badSeed = order with { ExitStudyPlan = seed with { EntryAtr = 0 } };
        var invalid = TradingExitStudy.Advance(
            state with
            {
                Paper = paper with { Orders = [badSeed] },
            },
            [stopBook],
            stopAt
        );
        if (invalid.ExitStudies.Single().Status != "unknown" || invalid.Mode != state.Mode)
        {
            throw new InvalidOperationException("Invalid research seeds cannot fault or arm execution.");
        }

        var stale = entryBook with { ObservedAt = seededNow.AddMinutes(-10) };
        var staleState = TradingExitStudy.Advance(
            state with
            {
                Rules = rules,
                Paper = paper,
            },
            [stale],
            seededNow.AddMinutes(1)
        );
        if (staleState.ExitStudies.Single().HypotheticalNetThb is not null)
        {
            throw new InvalidOperationException("Stale quotes must not invent a hypothetical exit.");
        }

        var separated = state with
        {
            Rules = rules,
            Paper = paper,
            Live = state.Live with { Orders = [order], Fills = [fill] },
        };
        var separatedResult = TradingExitStudy.Advance(separated, [entryBook], seededNow.AddMinutes(1));
        if (separatedResult.ExitStudies.Select(item => item.SourceMode).Distinct().Count() != 2)
        {
            throw new InvalidOperationException("Paper and live ledgers must remain separate study sources.");
        }

        return Task.CompletedTask;
    }

    private static TradingBook Book(DateTimeOffset now, decimal bid)
    {
        var end = now.ToUnixTimeSeconds() / 300 * 300;
        var bars = Enumerable
            .Range(0, 51)
            .Select(index =>
            {
                var close = 90m + index * 0.25m;
                return new TradingBar(end - (51 - index) * 300, close, close + 1, close - 1, close, 1);
            })
            .ToArray();
        return new TradingBook("BTC", now, bid, bid + 0.01m, 10m, 10m, bars);
    }
}
