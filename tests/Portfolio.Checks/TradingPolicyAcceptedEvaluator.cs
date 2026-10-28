using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class TradingPolicyAcceptedEvaluator : ITradingPolicyEvaluator
{
    private readonly DateTimeOffset observedAt;

    public TradingPolicyAcceptedEvaluator(DateTimeOffset observedAt) => this.observedAt = observedAt;

    public bool Accepted { get; init; } = true;

    public Action? BeforeReturn { get; init; }

    public int Calls { get; private set; }

    public Task<TradingPolicyEvaluation> Evaluate(
        TradingState state,
        TradingRuleSet proposed,
        CancellationToken cancellationToken
    )
    {
        this.Calls++;
        this.BeforeReturn?.Invoke();
        return Task.FromResult(
            new TradingPolicyEvaluation(
                this.Accepted,
                "test evaluation",
                this.observedAt,
                new string('a', 64),
                [],
                [],
                1,
                1
            )
            {
                BaselineRulesHash = state.RulesHash,
                CandidateRulesHash = TradingRuleHash.Compute(proposed),
            }
        );
    }
}
