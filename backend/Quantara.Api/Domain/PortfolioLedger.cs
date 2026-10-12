namespace Quantara.Api.Domain;

public sealed record Coin(string Symbol, string Name, string Source = "exchange");
public sealed record Balance(string Symbol, decimal Quantity, decimal? Available = null);
public sealed record Quote(string Symbol, decimal Price);
public sealed record Trade(string TransactionId, string Symbol, string Side,
    decimal Quantity, decimal Price, decimal Fee, decimal FeeCredit, DateTimeOffset Timestamp);

public sealed record PortfolioLedger
{
    public Coin[] Coins { get; init; } = [];
    public Balance[] Balances { get; init; } = [];
    public Quote[] Prices { get; init; } = [];
    public Trade[] Trades { get; init; } = [];
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? PriceUpdatedAt { get; init; }
    public DateTimeOffset? HistoryUpdatedAt { get; init; }
    public string[] UnavailableHistorySymbols { get; init; } = [];

    public PortfolioLedger Remember(Coin coin, IEnumerable<Trade> trades)
    {
        var mergedTrades = Trades.Concat(trades)
            .DistinctBy(trade => (trade.Symbol, trade.TransactionId)).ToArray();
        var coins = Coins.Concat([coin]).DistinctBy(item => item.Symbol).ToArray();
        return this with { Coins = coins, Trades = mergedTrades };
    }
}

public sealed record Position(string Symbol, string Name, string Status, decimal Quantity,
    decimal? AverageCost, decimal? CurrentPrice, decimal? Value,
    decimal? UnrealizedPnl, decimal? RealizedPnl, string CostStatus);

public sealed record PortfolioSummary(decimal? TotalAssets, decimal? AvailableThb, int? HoldingCount);

public static class PortfolioProjection
{
    public static PortfolioSummary Summary(PortfolioLedger ledger)
    {
        if (ledger.UpdatedAt is null) return new(null, null, null);

        var held = ledger.Balances.Where(balance => balance.Symbol != "THB" && balance.Quantity > 0).ToArray();
        decimal? totalAssets = held.Length == 0
            ? 0
            : held.All(balance => ledger.Prices.Any(price => price.Symbol == balance.Symbol && price.Price > 0))
                ? held.Sum(balance => balance.Quantity * ledger.Prices.Single(price => price.Symbol == balance.Symbol).Price)
                : null;
        var thb = ledger.Balances.FirstOrDefault(balance => balance.Symbol == "THB");
        return new(totalAssets, thb?.Available, held.Length);
    }

    public static Position[] Positions(PortfolioLedger ledger) => ledger.Coins.Select(coin =>
    {
        var quantity = ledger.Balances.FirstOrDefault(balance => balance.Symbol == coin.Symbol)?.Quantity ?? 0;
        var price = ledger.Prices.FirstOrDefault(quote => quote.Symbol == coin.Symbol)?.Price;
        return new Position(coin.Symbol, coin.Name, quantity > 0 ? "Holding" : "Closed", quantity,
            null, price, quantity == 0 ? 0 : price * quantity, null, null, "Unknown");
    }).OrderBy(position => position.Status == "Closed").ThenBy(position => position.Symbol).ToArray();
}
