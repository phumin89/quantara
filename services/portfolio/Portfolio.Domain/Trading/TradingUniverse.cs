namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingUniverse(
    DateTimeOffset ObservedAt,
    string[] Symbols,
    int TotalMarkets,
    int ExcludedMarkets,
    string? Error
);
