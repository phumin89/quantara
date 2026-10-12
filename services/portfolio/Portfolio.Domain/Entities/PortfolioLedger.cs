namespace Quantara.Portfolio.Domain;

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

    public PortfolioLedger Remember(Coin coin, IEnumerable<Trade> trades) => this with
    {
        Coins = this.Coins.Concat([coin]).DistinctBy(item => item.Symbol).ToArray(),
        Trades = this.Trades.Concat(trades).DistinctBy(item => (item.Symbol, item.TransactionId)).ToArray()
    };
}
