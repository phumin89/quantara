using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingPolicyEvaluator
{
    public Task<TradingPolicyEvaluation> Evaluate(
        TradingState state,
        TradingRuleSet proposed,
        CancellationToken cancellationToken
    );
}
