using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingShadowChecks
{
    public static Task Run()
    {
        using var fixture = new AutonomousFixture();
        var now = fixture.Time.Now;
        var sample = new TradingShadow(
            "sample",
            "ETH",
            "breakout-v1",
            now,
            now.AddMinutes(15),
            120,
            90,
            100,
            95,
            105,
            2,
            100,
            25,
            8
        );
        var quote = fixture.Book("ETH") with { Bid = 99.9m, Ask = 100 };
        Check(
            TradingShadowProtocol.Advance(sample, quote, now).State == TradingShadowState.Waiting,
            "A captured quote cannot retrospectively fill its own sample."
        );
        var next = quote with { ObservedAt = now.AddSeconds(10) };
        var entered = TradingShadowProtocol.Advance(sample, next, now.AddSeconds(10));
        Check(
            entered.State == TradingShadowState.Open
                && entered.EntryCostThb <= 100
                && entered.EntryCostThb > entered.Quantity * 100,
            "A later executable book models an entry with visible liquidity and fees."
        );
        var target = next with { Bid = 106, Ask = 106.1m, ObservedAt = now.AddSeconds(20) };
        var closed = TradingShadowProtocol.Advance(entered, target, now.AddSeconds(20));
        Check(
            closed.State == TradingShadowState.Closed
                && closed.NetThb > 0
                && closed.NetThb < entered.Quantity * 106 - entered.EntryCostThb,
            "Benchmark returns include exit fees and never count as broker fills."
        );
        Check(
            TradingShadowProtocol.Advance(closed, target with { Bid = 1000 }, now.AddSeconds(30)) == closed,
            "Closed benchmark outcomes never change using hindsight."
        );
        Check(
            TradingShadowProtocol.Advance(entered, null, now.AddHours(3)).State == TradingShadowState.Unknown,
            "Missing exit evidence must remain Unknown."
        );
        Check(
            TradingShadowProtocol.Advance(sample, next with { AskQuantity = 0 }, now.AddSeconds(10)).State
                == TradingShadowState.Waiting,
            "Touching a price without observed liquidity never models a fill."
        );
        Check(
            TradingShadowProtocol.Advance(sample, null, now.AddMinutes(16)).State == TradingShadowState.Expired,
            "An unobserved entry expires without inventing a profit."
        );
        var state = fixture.Store.State with { Shadows = [closed] };
        Check(
            TradingOpportunityReview.Days(state, now).Single().Closed == 1 && state.Live.Fills.Count == 0,
            "Learning summaries remain separate from real-account performance."
        );
        var projected = new TradingOverviewProjection(
            fixture.Account,
            new("fixture-owner-token"),
            fixture.Time
        ).Serialize(state);
        using var json = System.Text.Json.JsonDocument.Parse(projected);
        Check(
            json.RootElement.GetProperty("rules")
                .GetProperty("adaptive")
                .GetProperty("risk")
                .GetProperty("profile")
                .GetString() == "aggressive",
            "Actual API rules must match the strict frontend enum contract."
        );
        Console.WriteLine("Frozen opportunity benchmark checks passed.");
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
