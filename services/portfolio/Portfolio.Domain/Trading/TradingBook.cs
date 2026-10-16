namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingBook(
    string Symbol,
    DateTimeOffset ObservedAt,
    decimal Bid,
    decimal Ask,
    decimal BidQuantity,
    decimal AskQuantity,
    IReadOnlyList<TradingBar> Bars,
    decimal MinimumNotional = 10,
    decimal PriceStep = 0.01m,
    int PriceScale = 2,
    int BaseScale = 8,
    int QuoteScale = 2,
    bool Enabled = true
);
