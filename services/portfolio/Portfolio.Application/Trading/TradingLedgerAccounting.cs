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
            if (!SameExecution(duplicate, fill))
            {
                throw new InvalidOperationException("Conflicting broker fill identity.");
            }

            return ledger;
        }

        if (fill.OccurredAt <= TradingCashBaselineAccounting.Cutoff(ledger))
        {
            throw new InvalidOperationException(
                "An unseen fill predates the reviewed cash baseline; reconcile without repeating its cash movement."
            );
        }

        var baseQuantity = CreditedQuantity(fill);
        if (
            fill.Quantity <= 0
            || baseQuantity <= 0
            || fill.Side == TradeSide.Sell && baseQuantity != fill.Quantity
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
                (holding?.Quantity ?? 0) + baseQuantity,
                (holding?.CostThb ?? 0) + cost,
                holding is { Quantity: > 0 } ? holding.OpenedAt : fill.OccurredAt,
                holding is { Quantity: > 0 } ? Math.Max(holding.PeakBid, fill.Price) : fill.Price,
                (holding?.EntryFeesThb ?? 0) + fill.FeeThb
            )
            {
                ExitPlan = holding is { Quantity: > 0 } ? holding.ExitPlan : null,
                StopPrice = holding is { Quantity: > 0 } ? holding.StopPrice : null,
                TargetPrice = holding is { Quantity: > 0 } ? holding.TargetPrice : null,
                TrailingDistance = holding is { Quantity: > 0 } ? holding.TrailingDistance : null,
            };
        }
        else
        {
            if (holding is null || holding.Quantity < fill.Quantity || holding.Quantity <= 0)
            {
                throw new InvalidOperationException("Sell fill exceeds verified strategy position.");
            }

            var closesPosition = ClosesPosition(holding.Quantity, fill);
            var releasedCost = closesPosition ? holding.CostThb : holding.CostThb * fill.Quantity / holding.Quantity;
            var releasedFees = closesPosition
                ? holding.EntryFeesThb
                : holding.EntryFeesThb * fill.Quantity / holding.Quantity;
            fill = fill with
            {
                RealizedGrossThb = fill.GrossThb - releasedCost + releasedFees,
                RealizedNetThb = fill.GrossThb - fill.FeeThb - releasedCost,
            };
            cash += fill.GrossThb - fill.FeeThb;
            updated = holding with
            {
                Quantity = closesPosition ? 0m : holding.Quantity - fill.Quantity,
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

    internal static decimal CreditedQuantity(TradingFill fill)
    {
        if (fill.QuantityScale is < 0 or > 18)
        {
            throw new InvalidOperationException("Fill base precision is invalid.");
        }

        return fill.QuantityScale is { } scale
            ? decimal.Round(fill.Quantity, scale, MidpointRounding.ToZero)
            : fill.Quantity;
    }

    internal static decimal? RemainingQuantity(IEnumerable<TradingFill> fills)
    {
        var quantity = 0m;
        foreach (var fill in fills.OrderBy(fill => fill.OccurredAt).ThenBy(fill => fill.Id))
        {
            if (fill.Side == TradeSide.Buy)
            {
                quantity += CreditedQuantity(fill);
            }
            else if (fill.Side == TradeSide.Sell && fill.Quantity <= quantity)
            {
                quantity = ClosesPosition(quantity, fill) ? 0m : quantity - fill.Quantity;
            }
            else
            {
                return null;
            }
        }

        return quantity;
    }

    // Retain raw fill evidence; discard only a legacy division residue below the verified broker base unit.
    internal static bool ClosesPosition(decimal quantity, TradingFill fill) =>
        fill.QuantityScale is { } scale && decimal.Round(quantity, scale, MidpointRounding.ToZero) == fill.Quantity;

    public static bool SameExecution(TradingFill first, TradingFill second) =>
        first.BrokerFillId == second.BrokerFillId
        && first.IntentId == second.IntentId
        && first.Quantity == second.Quantity
        && first.GrossThb == second.GrossThb
        && first.FeeThb == second.FeeThb
        && first.Price == second.Price
        && first.Side == second.Side
        && first.Symbol == second.Symbol
        && first.OccurredAt == second.OccurredAt
        && first.GrossFeeThb == second.GrossFeeThb
        && first.FeeCreditThb == second.FeeCreditThb
        && (first.QuantityScale is null || second.QuantityScale is null || first.QuantityScale == second.QuantityScale);

    public static decimal ReservedCash(TradingLedger ledger) =>
        ledger.Orders.Where(order => Active(order) && order.Side == TradeSide.Buy).Sum(order => order.Remaining);

    public static bool Active(TradingIntent order) =>
        order.Status is "pending" or "submitting" or "submitted" or "partial" or "unknown";

    public static bool Unresolved(TradingIntent order) =>
        Active(order) || order.CancellationState is "claimed" or "unknown";

    public static decimal? Equity(
        TradingLedger ledger,
        IReadOnlyList<TradingBook> books,
        DateTimeOffset now,
        int freshnessSeconds
    )
    {
        if (TradingCashBaselineAccounting.CashUnknown(ledger))
        {
            return null;
        }

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

    internal static TradingLedger ReplaceOrder(TradingLedger ledger, TradingIntent intent) =>
        ledger with
        {
            Orders = ledger.Orders.Select(order => order.Id == intent.Id ? intent : order).ToArray(),
        };
}
