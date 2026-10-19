using Quantara.Portfolio.Application;
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

        Console.WriteLine("Trading watch selection checks passed.");
        return Task.CompletedTask;
    }
}
