namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingDecisionRecord(
    string Key,
    string Symbol,
    long Candle,
    TradingAction Action,
    string Reason,
    DateTimeOffset At,
    TradingMode Mode
);
