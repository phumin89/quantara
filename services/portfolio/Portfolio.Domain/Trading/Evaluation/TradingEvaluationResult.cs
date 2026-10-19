namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingEvaluationResult(
    string Protocol,
    bool VerifiedProfit,
    IReadOnlyList<TradingEvaluationMetric> Metrics,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ObservedAt,
    string RulesHash
);
