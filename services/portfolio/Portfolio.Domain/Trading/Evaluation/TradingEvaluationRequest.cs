namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingEvaluationRequest(
    IReadOnlyList<string> Symbols,
    int TimeframeSeconds = 0,
    TradingRuleSet? Rules = null,
    decimal ReferenceBudgetThb = 1000m
);
