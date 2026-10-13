namespace Quantara.MarketData.Domain;

public sealed record MarketCandle(
    long Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume
);
