using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingPolicyCandidateReader
{
    public Task<TradingPolicyCandidate?> Read(CancellationToken cancellationToken);
}
