using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public interface IReadOnlyRiskStore
{
    public Task<RiskPolicyState> ReadPolicy(CancellationToken cancellationToken);

    public Task<CapitalSnapshot?> ReadCapital(CancellationToken cancellationToken);

    public Task<RiskPerformance?> ReadPerformance(CancellationToken cancellationToken);
}
