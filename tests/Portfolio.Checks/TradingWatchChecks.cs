using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingWatchChecks
{
    public static Task Run()
    {
        using var fixture = new AutonomousFixture();
        var now = fixture.Time.Now;
        var state = fixture.Store.State with
        {
            Rules = fixture.Store.State.Rules with
            {
                Version = "adaptive-intraday-v1",
                Symbols = [],
                Adaptive = new() { Risk = new() { Profile = Quantara.Portfolio.Domain.Risk.RiskProfile.Aggressive } },
            },
            Autonomy = new(
                now,
                now,
                4,
                4,
                [
                    new("USDT", now, "excluded", "adaptive-intraday-v1", 100),
                    new("BTC", now.AddMinutes(-20), "stale", null, 100),
                    new("ETH", now.AddMinutes(1), "future", null, 100),
                    new("KUB", now, "fresh", null, 2),
                    new("KUB", now.AddSeconds(-1), "older", null, 90),
                ],
                null,
                "ok",
                now
            ),
        };
        var candidates = TradingWatchSelection.Candidates(state, now);
        if (candidates.Count != 1 || candidates[0].Symbol != "KUB" || candidates[0].Score != 2)
        {
            throw new InvalidOperationException(
                "Watch candidates must deduplicate and exclude stale, future, and excluded observations."
            );
        }

        var owned = state with
        {
            Live = state.Live with
            {
                Positions = [new TradingHolding("USDT", 1, 30, now, 30)],
                Orders =
                [
                    new TradingIntent(
                        Guid.NewGuid(),
                        "FIL",
                        Quantara.Portfolio.Domain.TradeSide.Buy,
                        100,
                        10,
                        100,
                        now,
                        0,
                        state.RulesHash,
                        "fixture",
                        "submitted"
                    ),
                ],
            },
        };
        if (!TradingWatchSelection.Required(owned, now).SequenceEqual(["FIL", "KUB", "USDT"]))
        {
            throw new InvalidOperationException(
                "Owned excluded assets and active orders stay pinned before research candidates."
            );
        }

        var full = owned with
        {
            Live = owned.Live with
            {
                Positions = Enumerable
                    .Range(0, 64)
                    .Select(index => new TradingHolding("C" + index, 1, 100, now, 100))
                    .ToArray(),
                Orders = [],
            },
        };
        if (
            TradingWatchSelection.Required(full, now).Count != 64
            || TradingWatchSelection.Required(full, now).Contains("KUB")
        )
        {
            throw new InvalidOperationException("Research candidates never evict owned symbols or exceed 64 watches.");
        }

        var required = TradingWatchSelection.Required(state, now);
        if (!required.SequenceEqual(["KUB"]))
        {
            throw new InvalidOperationException(
                "Adaptive required selection must retain only positive fresh candidates."
            );
        }

        TradingWatchChecks.CheckCandidateSlots(state, now);
        Console.WriteLine(
            "Trading watch selection checks passed: fresh ranking, pinned-symbol deduplication and bounded distinct candidate slots."
        );
        return Task.CompletedTask;
    }

    private static void CheckCandidateSlots(TradingState state, DateTimeOffset now)
    {
        var status = state.Autonomy ?? throw new InvalidOperationException("Watch status fixture is missing.");
        var pinned = Enumerable.Range(0, 8).Select(index => "H" + index).ToArray();
        var entrants = Enumerable.Range(0, 8).Select(index => "N" + index).ToArray();
        var observations = pinned
            .Select((symbol, index) => new TradingScanObservation(symbol, now, "pinned", null, 100 - index))
            .Concat(entrants.Select((symbol, index) => new TradingScanObservation(symbol, now, "new", null, 8 - index)))
            .Append(new TradingScanObservation("N8", now, "outside the top sixteen", null, 0.5m))
            .ToArray();
        var ranked = state with
        {
            Autonomy = status with { TotalMarkets = 17, EligibleMarkets = 17, Markets = observations },
        };
        var required = pinned.Concat(entrants).ToArray();
        var owned = ranked with
        {
            Live = ranked.Live with
            {
                Positions = pinned.Take(4).Select(symbol => new TradingHolding(symbol, 1, 100, now, 100)).ToArray(),
            },
            Paper = ranked.Paper with
            {
                Orders = pinned
                    .Skip(4)
                    .Select(symbol => new TradingIntent(
                        Guid.NewGuid(),
                        symbol,
                        TradeSide.Buy,
                        100,
                        10,
                        100,
                        now,
                        0,
                        state.RulesHash,
                        "watch-only fixture",
                        "submitted"
                    ))
                    .ToArray(),
            },
        };
        TradingWatchChecks.CheckSelection(owned, now, required);
        var research = ranked with
        {
            ExitStudies = pinned
                .Select(
                    (symbol, index) =>
                        new TradingExitStudySample
                        {
                            Symbol = symbol,
                            CreatedAt = now,
                            Status = index % 2 == 0 ? "active" : "triggered",
                        }
                )
                .ToArray(),
        };
        TradingWatchChecks.CheckSelection(research, now, required);
        TradingWatchChecks.CheckSelection(
            owned with
            {
                Paper = ranked.Paper,
                ExitStudies = research.ExitStudies.Skip(4).ToArray(),
            },
            now,
            required
        );

        var crowded = ranked with
        {
            Live = ranked.Live with
            {
                Positions = Enumerable
                    .Range(0, 62)
                    .Select(index => new TradingHolding("C" + index, 1, 100, now, 100))
                    .ToArray(),
            },
            ExitStudies = [new TradingExitStudySample { Symbol = "R0", CreatedAt = now }],
            Autonomy = status with
            {
                Markets =
                [
                    new("C0", now, "owned", null, 100),
                    new("R0", now, "research", null, 99),
                    new("N0", now, "first entrant", null, 98),
                    new("N1", now, "beyond capacity", null, 97),
                ],
            },
        };
        TradingWatchChecks.CheckSelection(
            crowded,
            now,
            Enumerable.Range(0, 62).Select(index => "C" + index).Concat(["R0", "N0"]).ToArray()
        );
        TradingWatchChecks.CheckSelection(ranked, now, pinned);
        var horizon = ranked with
        {
            Live = ranked.Live with
            {
                Positions = Enumerable
                    .Range(0, 16)
                    .Select(index => new TradingHolding("H" + index, 1, 100, now, 100))
                    .ToArray(),
            },
            Autonomy = status with
            {
                Markets = Enumerable
                    .Range(0, 16)
                    .Select(index => new TradingScanObservation("H" + index, now, "pinned", null, 100 - index))
                    .Append(new("N0", now, "rank seventeen", null, 1))
                    .ToArray(),
            },
        };
        TradingWatchChecks.CheckSelection(horizon, now, Enumerable.Range(0, 16).Select(index => "H" + index).ToArray());
        TradingWatchChecks.CheckSelection(
            ranked with
            {
                Rules = ranked.Rules with
                {
                    Version = "ema20-50-breakout-v1",
                    Adaptive = null,
                    Symbols = ["Z0", "BTC"],
                },
            },
            now,
            ["BTC", "Z0"]
        );
    }

    private static void CheckSelection(TradingState state, DateTimeOffset now, IReadOnlyList<string> expected)
    {
        var before = JsonSerializer.Serialize(state);
        var selected = TradingWatchSelection.Required(state, now);
        if (
            !selected.SequenceEqual(expected.Order(StringComparer.Ordinal))
            || selected.Distinct(StringComparer.Ordinal).Count() != selected.Count
            || selected.Count > 64
            || JsonSerializer.Serialize(state) != before
        )
        {
            throw new InvalidOperationException(
                "Watch selection must preserve state and pinned priority, filling only distinct candidate slots within its existing horizon and capacity."
            );
        }
    }
}
