using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class TradingPolicyCandidateReader(TradingPolicyCandidate candidate) : ITradingPolicyCandidateReader
{
    public Task<TradingPolicyCandidate?> Read(CancellationToken cancellationToken) =>
        Task.FromResult<TradingPolicyCandidate?>(candidate);
}
