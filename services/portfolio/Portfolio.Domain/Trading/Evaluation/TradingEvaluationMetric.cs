namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingEvaluationMetric(
    string Symbol,
    int TimeframeSeconds,
    string Segment,
    string Strategy,
    int Samples,
    int Trades,
    int UnknownFills,
    decimal? NetThb,
    decimal FeesThb,
    decimal? MaxDrawdownThb,
    decimal CashBenchmarkThb,
    bool InsufficientSamples
);
