using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class PortfolioLastBuyChecks
{
    public static async Task Run()
    {
        var at = DateTimeOffset.UtcNow;
        var intent = Guid.NewGuid();
        var laterIntent = Guid.NewGuid();
        var fills = new[]
        {
            new TradingFill(Guid.NewGuid(), intent, "a", "ETH", TradeSide.Buy, 2, 100, 200, 1, at.AddMinutes(-5)),
            new TradingFill(Guid.NewGuid(), intent, "b", "ETH", TradeSide.Buy, 1, 120, 120, 1, at.AddMinutes(-4)),
            new TradingFill(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "sell",
                "ETH",
                TradeSide.Sell,
                1,
                200,
                200,
                1,
                at.AddMinutes(-3)
            ),
            new TradingFill(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "paper",
                "ETH",
                TradeSide.Buy,
                1,
                999,
                999,
                1,
                at.AddMinutes(-2)
            ),
            new TradingFill(Guid.NewGuid(), laterIntent, "c", "ETH", TradeSide.Buy, 1, 150, 150, 1, at.AddMinutes(-1)),
        };
        var state = TradingStateFactory.Create(at) with
        {
            Live = TradingStateFactory.Create(at).Live with { Fills = [fills[0], fills[1], fills[2], fills[4]] },
            Paper = TradingStateFactory.Create(at).Paper with { Fills = [fills[3]] },
        };
        var ledger = new PortfolioLedger { Coins = [new Coin("ETH", "Ethereum")], Balances = [new Balance("ETH", 2)] };
        var view = await new GetPortfolioHandler(
            new PortfolioTestRepository(ledger),
            new PortfolioTestSync(),
            new TradingTestRepository(state)
        ).Handle(new GetPortfolioQuery(), CancellationToken.None);
        var position = view.Positions.Single();
        if (
            position.LastBuyPrice != 150m
            || position.LastBoughtAt != at.AddMinutes(-1)
            || position.AverageCost is not null
        )
        {
            throw new InvalidOperationException(
                "Portfolio last buy must use live buy fills only and preserve unknown wallet cost."
            );
        }

        var partial = await new GetPortfolioHandler(
            new PortfolioTestRepository(ledger),
            new PortfolioTestSync(),
            new TradingTestRepository(state with { Live = state.Live with { Fills = fills[..3] } })
        ).Handle(new GetPortfolioQuery(), CancellationToken.None);
        if (
            partial.Positions.Single().LastBuyPrice != 320m / 3m
            || partial.Positions.Single().LastBoughtAt != at.AddMinutes(-4)
        )
        {
            throw new InvalidOperationException(
                "Multiple confirmed fills under one intent use gross-weighted actual purchase price, excluding fees."
            );
        }

        var empty = await new GetPortfolioHandler(
            new PortfolioTestRepository(ledger),
            new PortfolioTestSync(),
            new TradingTestRepository(state with { Live = state.Live with { Fills = [] } })
        ).Handle(new GetPortfolioQuery(), CancellationToken.None);
        if (empty.Positions.Single().LastBuyPrice is not null)
        {
            throw new InvalidOperationException("Portfolio last buy must remain unavailable without confirmed fills.");
        }

        Console.WriteLine("Portfolio last buy checks passed.");
    }
}
