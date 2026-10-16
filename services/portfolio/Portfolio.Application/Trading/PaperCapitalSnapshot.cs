using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class PaperCapitalSnapshot
{
    public static CapitalSnapshot Create(
        string accountKey,
        TradingLedger ledger,
        IReadOnlyList<TradingBook> books,
        TradingRuleSet rules,
        DateTimeOffset now
    )
    {
        var active = ledger.Orders.Where(TradingLedgerAccounting.Active).ToArray();
        var reserved = active
            .Where(order => order.Side == TradeSide.Buy)
            .Sum(order => order.Remaining * (1 + rules.FeeBps / 10000m));
        var equity = TradingLedgerAccounting.Equity(ledger, books, now, rules.MaxDataAgeSeconds);
        var symbols = ledger
            .Positions.Select(position => position.Symbol)
            .Concat(active.Select(order => order.Symbol))
            .Concat(books.Select(book => book.Symbol))
            .Distinct(StringComparer.Ordinal);
        var assets = symbols
            .Select(symbol => new CapitalAsset(
                symbol,
                ledger.Positions.SingleOrDefault(position => position.Symbol == symbol)?.Quantity ?? 0,
                ledger.Positions.SingleOrDefault(position => position.Symbol == symbol)?.Quantity ?? 0,
                books.SingleOrDefault(book => book.Symbol == symbol)?.Bid,
                active
                    .Where(order => order.Symbol == symbol && order.Side == TradeSide.Sell)
                    .Sum(order => order.Remaining),
                active
                    .Where(order => order.Symbol == symbol && order.Side == TradeSide.Buy)
                    .Sum(order => order.Remaining)
            ))
            .ToArray();
        return new(
            accountKey,
            now,
            decimal.Max(0, ledger.CashThb - reserved),
            equity,
            equity - ledger.CashThb + reserved,
            reserved,
            assets
                .Append(new CapitalAsset("THB", ledger.CashThb, decimal.Max(0, ledger.CashThb - reserved), 1, 0))
                .ToArray(),
            false,
            equity is null ? "Simulated positions require fresh valuation." : null
        );
    }
}
