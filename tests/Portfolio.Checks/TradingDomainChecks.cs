using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingDomainChecks
{
    public static Task Run()
    {
        var rules = new TradingRuleSet();
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300 * 300);
        var bars = Enumerable
            .Range(0, 51)
            .Select(i => new TradingBar(
                now.AddMinutes(-5 * (51 - i)).ToUnixTimeSeconds(),
                100m,
                i == 50 ? 111m : 101m,
                99m,
                i == 50 ? 110m : 100m,
                i == 50 ? 2m : 1m
            ))
            .ToArray();
        var book = new TradingBook("BTC", now, 109.9m, 110m, 1m, 1m, bars);
        Check(
            TradingStrategy.Evaluate(rules, book, null, now).Action == TradingAction.Buy,
            "breakout entry uses seeded EMA history"
        );
        var extended = Enumerable
            .Range(0, 80)
            .Select(index => new TradingBar(
                now.AddMinutes(-5 * (80 - index)).ToUnixTimeSeconds(),
                100,
                index == 79 ? 111 : 101,
                99,
                index == 79 ? 110 : 100,
                index == 79 ? 2 : 1
            ))
            .ToArray();
        Check(
            TradingStrategy
                .Evaluate(rules, book with { Bars = extended.Where((_, index) => index != 10).ToArray() }, null, now)
                .Action == TradingAction.Buy,
            "An older missing bar cannot invalidate 69 verified recent contiguous candles."
        );
        Check(
            TradingStrategy
                .Evaluate(rules, book with { Bars = extended.Where((_, index) => index != 75).ToArray() }, null, now)
                .Action == TradingAction.Wait,
            "A recent gap must wait for at least 51 contiguous completed candles."
        );
        var holding = new TradingHolding("BTC", 1m, 100m, now.AddMinutes(-1), 100m);
        Check(
            TradingStrategy.Evaluate(rules, book with { Bid = 100m, Ask = 100.1m }, holding, now).Action
                == TradingAction.Wait,
            "held position does not re-enter"
        );
        Check(
            TradingStrategy.Evaluate(rules, book with { Bid = 98m, Ask = 98.1m }, holding, now).Action
                == TradingAction.Sell,
            "stop exit is independent"
        );
        Check(
            PaperFillCalculator.Fill(TradeSide.Buy, 100m, 110m, book, rules, 3) is { BaseQuantity: > 0m },
            "paper buy fills at ask"
        );
        Check(PaperFillCalculator.Fill(TradeSide.Buy, 10m, 109m, book, rules, 3) is null, "paper buy respects limit");
        Console.WriteLine("Trading domain checks passed.");
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
