using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Application;

public sealed record MarketEventBook(
    string Symbol,
    DateTimeOffset ObservedAt,
    decimal Bid,
    decimal Ask,
    decimal BidQuantity,
    decimal AskQuantity,
    IReadOnlyList<MarketCandle> Bars,
    decimal MinimumNotional,
    decimal PriceStep,
    int PriceScale,
    int BaseScale,
    int QuoteScale,
    bool Enabled,
    bool SellEnabled
);
