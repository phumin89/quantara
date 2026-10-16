namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingDecision(
    TradingAction Action,
    string Reason,
    long Candle,
    decimal ReferencePrice,
    decimal? EntryLimit = null,
    decimal? StopPrice = null,
    long? StopCandle = null,
    string? StrategyVersion = null
);
