namespace Quantara.MarketData.Domain;

public sealed record IntradaySeries(
    string Symbol,
    IReadOnlyList<MarketCandle> Candles,
    DateTimeOffset FetchedAt,
    string? Error
);
