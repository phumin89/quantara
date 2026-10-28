namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingPolicyChange(
    TradingPolicyAuthorization Authorization,
    TradingPolicyCandidate Candidate,
    TradingRuleSet PreviousRules,
    TradingRuleSet AppliedRules,
    TradingPolicyEvaluation Evaluation,
    DateTimeOffset AppliedAt
);
