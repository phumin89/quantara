namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingScanObservation(
    string Symbol,
    DateTimeOffset At,
    string Reason,
    string? StrategyVersion,
    decimal? Score = null
);
