using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingAccountingChecks
{
    public static Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var ledger = TradingStateFactory.Create(now).Paper;
        var buy = new TradingFill(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buy",
            "BTC",
            TradeSide.Buy,
            1,
            100,
            100,
            1,
            now,
            GrossFeeThb: 2,
            FeeCreditThb: 1
        );
        ledger = TradingLedgerAccounting.Apply(ledger, buy);
        var duplicate = TradingLedgerAccounting.Apply(ledger, buy);
        Check(
            duplicate == ledger && ledger.CashThb == 899 && ledger.Positions.Single().CostThb == 101,
            "Buy costs include actual net fees, and duplicate evidence cannot debit cash twice."
        );
        var conflict = false;
        try
        {
            TradingLedgerAccounting.Apply(ledger, buy with { GrossFeeThb = 3, FeeCreditThb = 2 });
        }
        catch (InvalidOperationException)
        {
            conflict = true;
        }

        Check(conflict, "Conflicting raw fee credits are rejected even when the net fee matches.");
        var sell = new TradingFill(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "sell",
            "BTC",
            TradeSide.Sell,
            1,
            110,
            110,
            1,
            now.AddMinutes(1)
        );
        ledger = TradingLedgerAccounting.Apply(ledger, sell);
        Check(
            ledger.CashThb == 1008
                && ledger.Fills[^1].RealizedGrossThb == 10
                && ledger.Fills[^1].RealizedNetThb == 8
                && ledger.Positions.Single().Quantity == 0,
            "A completed round trip retains its closed position and reconciles gross, net and both fees."
        );
        var reopened = TradingLedgerAccounting.Apply(
            ledger,
            buy with
            {
                Id = Guid.NewGuid(),
                BrokerFillId = "reopen",
                IntentId = Guid.NewGuid(),
                Price = 90,
                GrossThb = 90,
                OccurredAt = now.AddMinutes(2),
            }
        );
        Check(
            reopened.Positions.Single().PeakBid == 90 && reopened.Positions.Single().OpenedAt == now.AddMinutes(2),
            "Reopening a closed position starts a new peak and time-exit clock."
        );
        Console.WriteLine(
            "Trading accounting checks passed: exact fees, immutable duplicates, round-trip P&L and reopened position basis."
        );
        return Task.CompletedTask;
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
