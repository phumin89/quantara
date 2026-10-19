using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingPrecisionChecks
{
    public static async Task Run()
    {
        Accounting();
        foreach (var shortage in new[] { false, true })
        {
            using var fixture = new AutonomousFixture();
            await fixture.Activate();
            const decimal quantity = 1877.22m / 43.34m;
            const decimal spendable = 43.31379787m;
            fixture.Store.State = fixture.Store.State with
            {
                Live = fixture.Store.State.Live with
                {
                    CashThb = 3152.21m,
                    Positions =
                    [
                        new("ETH", quantity, 1877.22m, fixture.Time.Now.AddMinutes(-5), 110m) { StopPrice = 109m },
                    ],
                },
            };
            fixture.Exchange.AvailableBase = shortage ? spendable - 0.00000001m : spendable;
            fixture.Markets.Books = [fixture.Book("ETH") with { Bid = 108m, Ask = 108.01m }];
            await fixture.Engine.Tick(CancellationToken.None);
            if (
                fixture.Exchange.Submissions != (shortage ? 0 : 1)
                || fixture.Store.State.Live.Positions.Single().Quantity != quantity
            )
            {
                throw new InvalidOperationException(
                    "Verified market precision must permit legacy division residue, reject a full base-unit shortage, and retain ownership until a confirmed fill."
                );
            }
        }

        Console.WriteLine(
            "Trading precision: legacy division residue permits safe exits; genuine shortages remain blocked."
        );
    }

    private static void Accounting()
    {
        var now = DateTimeOffset.UtcNow;
        const decimal rawQuantity = 1877.22m / 43.34m;
        const decimal creditedQuantity = 43.31379787m;
        var ledger = TradingStateFactory.Create(now).Live with { FundingThb = 5000m, CashThb = 5000m };
        var buy = new TradingFill(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "precision-buy",
            "XRP",
            TradeSide.Buy,
            rawQuantity,
            43.34m,
            1877.22m,
            0m,
            now
        )
        {
            QuantityScale = 8,
        };
        ledger = TradingLedgerAccounting.Apply(ledger, buy);
        if (
            ledger.Positions.Single().Quantity != creditedQuantity
            || ledger.Fills.Single().Quantity != rawQuantity
            || TradingLedgerAccounting.Apply(ledger, buy) != ledger
        )
        {
            throw new InvalidOperationException(
                "Holdings use broker precision while raw fill evidence, exact cost and deduplication are retained."
            );
        }

        var legacy = ledger with { Positions = [ledger.Positions.Single() with { Quantity = rawQuantity }] };
        var sell = new TradingFill(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "precision-sell",
            "XRP",
            TradeSide.Sell,
            creditedQuantity,
            42.96m,
            creditedQuantity * 42.96m,
            0m,
            now
        )
        {
            QuantityScale = 8,
        };
        var closed = TradingLedgerAccounting.Apply(legacy, sell);
        if (
            closed.Positions.Single().Quantity != 0m
            || closed.Positions.Single().CostThb != 0m
            || closed.Fills[^1].RealizedNetThb != sell.GrossThb - 1877.22m
            || closed.Fills[0] != buy
        )
        {
            throw new InvalidOperationException(
                "Confirmed full-base-unit exits close legacy division residue and release the entire original cost without rewriting fills."
            );
        }

        var partial = TradingLedgerAccounting.Apply(
            legacy,
            sell with
            {
                BrokerFillId = "precision-partial",
                Quantity = 10m,
                GrossThb = 429.6m,
            }
        );
        if (partial.Positions.Single().Quantity != rawQuantity - 10m)
        {
            throw new InvalidOperationException("Partial fills must retain the unsold position.");
        }
    }
}
