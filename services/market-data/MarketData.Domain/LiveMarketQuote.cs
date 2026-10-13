namespace Quantara.MarketData.Domain;

public sealed record LiveMarketQuote(
    string Symbol,
    decimal Bid,
    decimal Ask,
    decimal Last,
    DateTimeOffset ObservedAt,
    string? BookEventJson,
    DateTimeOffset? BookObservedAt = null
);
