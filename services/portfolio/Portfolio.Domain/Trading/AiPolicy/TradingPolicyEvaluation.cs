using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingPolicyEvaluation(
    bool Accepted,
    string Reason,
    DateTimeOffset ObservedAt,
    string DatasetHash,
    IReadOnlyList<TradingEvaluationMetric> Baseline,
    IReadOnlyList<TradingEvaluationMetric> Candidate,
    decimal? MaximumScenarioSpendThb,
    decimal? MaximumScenarioRiskThb
)
{
    public IReadOnlyList<RiskSizingSnapshot> RiskScenarios { get; init; } = [];

    public string BaselineRulesHash { get; init; } = "";

    public string CandidateRulesHash { get; init; } = "";
}
