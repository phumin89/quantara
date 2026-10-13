namespace Quantara.MarketData.Domain;

public sealed record IntradaySnapshot(
    bool Available,
    DateTimeOffset CheckedAt,
    bool Connected,
    IReadOnlyList<LiveMarketQuote> Quotes,
    IReadOnlyList<IntradaySeries> Series,
    IReadOnlyList<MarketSignal> Signals,
    string? Error
);
