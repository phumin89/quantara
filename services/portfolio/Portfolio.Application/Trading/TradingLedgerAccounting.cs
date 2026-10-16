using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingLedgerAccounting
{
    public static TradingLedger Apply(TradingLedger ledger, TradingFill fill)
    {
        var duplicate = ledger.Fills.FirstOrDefault(item => item.BrokerFillId == fill.BrokerFillId);
        if (duplicate is not null)
        {
            if (
                duplicate.IntentId != fill.IntentId
                || duplicate.Quantity != fill.Quantity
                || duplicate.GrossThb != fill.GrossThb
                || duplicate.FeeThb != fill.FeeThb
                || duplicate.Price != fill.Price
                || duplicate.Side != fill.Side
                || duplicate.Symbol != fill.Symbol
                || duplicate.OccurredAt != fill.OccurredAt
                || duplicate.GrossFeeThb != fill.GrossFeeThb
                || duplicate.FeeCreditThb != fill.FeeCreditThb
            )
            {
                throw new InvalidOperationException("Conflicting broker fill identity.");
            }

            return ledger;
        }

        if (
            fill.Quantity <= 0
            || fill.Price <= 0
            || fill.GrossThb <= 0
            || fill.FeeThb < 0
            || decimal.Abs(fill.Quantity * fill.Price - fill.GrossThb) > 0.0000001m
            || !Enum.IsDefined(fill.Side)
        )
        {
            throw new InvalidOperationException("Fill amounts could not be reconciled.");
        }

        var holding = ledger.Positions.SingleOrDefault(item => item.Symbol == fill.Symbol);
        var cash = ledger.CashThb;
        TradingHolding updated;
        if (fill.Side == TradeSide.Buy)
        {
            var cost = fill.GrossThb + fill.FeeThb;
            if (cash < cost)
            {
                throw new InvalidOperationException("Fill exceeds allocated cash.");
            }

            cash -= cost;
            updated = new TradingHolding(
                fill.Symbol,
                (holding?.Quantity ?? 0) + fill.Quantity,
                (holding?.CostThb ?? 0) + cost,
                holding is { Quantity: > 0 } ? holding.OpenedAt : fill.OccurredAt,
                holding is { Quantity: > 0 } ? Math.Max(holding.PeakBid, fill.Price) : fill.Price,
                (holding?.EntryFeesThb ?? 0) + fill.FeeThb
            );
        }
        else
        {
            if (holding is null || holding.Quantity < fill.Quantity || holding.Quantity <= 0)
            {
                throw new InvalidOperationException("Sell fill exceeds verified strategy position.");
            }

            var releasedCost = holding.CostThb * fill.Quantity / holding.Quantity;
            var releasedFees = holding.EntryFeesThb * fill.Quantity / holding.Quantity;
            fill = fill with
            {
                RealizedGrossThb = fill.GrossThb - releasedCost + releasedFees,
                RealizedNetThb = fill.GrossThb - fill.FeeThb - releasedCost,
            };
            cash += fill.GrossThb - fill.FeeThb;
            updated = holding with
            {
                Quantity = holding.Quantity - fill.Quantity,
                CostThb = holding.CostThb - releasedCost,
                EntryFeesThb = holding.EntryFeesThb - releasedFees,
            };
        }

        return ledger with
        {
            CashThb = cash,
            Positions = ledger.Positions.Where(item => item.Symbol != fill.Symbol).Append(updated).ToArray(),
            Fills = ledger.Fills.Append(fill).ToArray(),
        };
    }

    public static decimal ReservedCash(TradingLedger ledger) =>
        ledger.Orders.Where(order => Active(order) && order.Side == TradeSide.Buy).Sum(order => order.Remaining);

    public static bool Active(TradingIntent order) =>
        order.Status is "pending" or "submitting" or "submitted" or "partial" or "unknown";

    public static decimal? Equity(
        TradingLedger ledger,
        IReadOnlyList<TradingBook> books,
        DateTimeOffset now,
        int freshnessSeconds
    )
    {
        decimal value = ledger.CashThb;
        foreach (var holding in ledger.Positions.Where(position => position.Quantity > 0))
        {
            var book = books.SingleOrDefault(item => item.Symbol == holding.Symbol);
            if (
                book is null
                || book.Bid <= 0
                || now < book.ObservedAt
                || now - book.ObservedAt > TimeSpan.FromSeconds(freshnessSeconds)
            )
            {
                return null;
            }

            value += holding.Quantity * book.Bid;
        }

        return value;
    }
}
