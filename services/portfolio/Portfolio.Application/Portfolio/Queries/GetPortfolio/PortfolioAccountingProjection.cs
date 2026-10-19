using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class PortfolioAccountingProjection
{
    public static PortfolioAccountingSnapshot Build(PortfolioLedger wallet, TradingLedger live, DateTimeOffset now)
    {
        var symbols = live
            .Fills.Select(fill => fill.Symbol)
            .Concat(
                live.Positions.Where(position => position.Quantity > 0 || position.CostThb != 0)
                    .Select(position => position.Symbol)
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var conflicting = live
            .Fills.GroupBy(fill => fill.BrokerFillId, StringComparer.Ordinal)
            .Where(group => group.Select(fill => fill.Symbol).Distinct(StringComparer.Ordinal).Count() > 1)
            .SelectMany(group => group.Select(fill => fill.Symbol))
            .ToHashSet(StringComparer.Ordinal);
        var books = symbols.ToDictionary(
            symbol => symbol,
            symbol => conflicting.Contains(symbol) ? null : Replay(live, symbol, now),
            StringComparer.Ordinal
        );
        var positions = PortfolioProjection
            .Positions(wallet)
            .Select(position =>
            {
                if (!books.TryGetValue(position.Symbol, out var book) || book is null)
                {
                    return position;
                }

                var realized = book
                    .Fills.Where(fill => fill.Side == TradeSide.Sell)
                    .Sum(fill => fill.RealizedNetThb ?? 0);
                var holding = book.Positions.SingleOrDefault(item => item.Symbol == position.Symbol);
                var quantity = holding?.Quantity ?? 0;
                var latest = book.Fills.Max(fill => fill.OccurredAt);
                var retained = live.Positions.Where(item => item.Symbol == position.Symbol).ToArray();
                var reconciled =
                    retained.Length <= 1
                    && (retained.SingleOrDefault()?.Quantity ?? 0) == quantity
                    && (retained.SingleOrDefault()?.CostThb ?? 0) == (holding?.CostThb ?? 0)
                    && wallet.UpdatedAt is { } observed
                    && observed >= latest
                    && observed <= now
                    && position.Quantity == quantity;
                var priced =
                    wallet.PriceUpdatedAt is { } pricedAt
                    && pricedAt >= latest
                    && pricedAt <= now
                    && position.CurrentPrice is > 0;
                return position with
                {
                    AverageCost = reconciled && quantity > 0 ? holding?.AverageCost : null,
                    CostStatus = reconciled ? CostStatus.Verified : CostStatus.Unknown,
                    RealizedPnl = realized,
                    UnrealizedPnl =
                        reconciled && (quantity == 0 || priced)
                            ? quantity == 0
                                ? 0
                                : position.Value - holding?.CostThb
                            : null,
                };
            })
            .ToArray();
        var known = books.Values.All(book => book is not null);
        var realizedTotal = known
            ? books.Values.Sum(book =>
                book?.Fills.Where(fill => fill.Side == TradeSide.Sell).Sum(fill => fill.RealizedNetThb ?? 0) ?? 0
            )
            : (decimal?)null;
        var fees = known ? books.Values.Sum(book => book?.Fills.Sum(fill => fill.FeeThb) ?? 0) : (decimal?)null;
        var unreconciled = symbols
            .Where(symbol =>
                books[symbol] is null
                || books[symbol]?.Positions.Any(holding => holding.Quantity > 0) == true
                    && positions.SingleOrDefault(position => position.Symbol == symbol)?.UnrealizedPnl is null
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unrealized =
            known && unreconciled.Length == 0
                ? positions
                    .Where(position => books.ContainsKey(position.Symbol))
                    .Sum(position => position.UnrealizedPnl ?? 0)
                : (decimal?)null;
        return new(
            positions,
            new PortfolioPerformance(
                realizedTotal,
                unrealized,
                realizedTotal + unrealized,
                fees,
                live.Fills.Select(fill => (DateTimeOffset?)fill.OccurredAt)
                    .Append(wallet.PriceUpdatedAt)
                    .Where(at => at <= now)
                    .Max(),
                unreconciled,
                "Confirmed engine fills only. Buy costs include paid fees; unrealized P/L excludes future exit fees. Imported trades are not counted again. Account-wide cost and P/L require complete historical trades and transfers."
            )
        );
    }

    private static TradingLedger? Replay(TradingLedger live, string symbol, DateTimeOffset now)
    {
        var fills = live.Fills.Where(fill => fill.Symbol == symbol).ToArray();
        if (
            fills.Length == 0
            || fills.Any(fill => string.IsNullOrWhiteSpace(fill.BrokerFillId) || fill.OccurredAt > now)
        )
        {
            return null;
        }

        try
        {
            var funding = fills.Where(fill => fill.Side == TradeSide.Buy).Sum(fill => fill.GrossThb + fill.FeeThb);
            var book = new TradingLedger(funding, funding, live.OpenedAt, [], [], [], []);
            foreach (var fill in fills)
            {
                book = TradingLedgerAccounting.Apply(book, fill);
            }

            return book;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
