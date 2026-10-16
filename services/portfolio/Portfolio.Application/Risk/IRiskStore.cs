using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public interface IRiskStore : IReadOnlyRiskStore
{
    public Task Configure(RiskPolicy policy, string expectedHash, CancellationToken cancellationToken);

    public Task Observe(
        CapitalSnapshot snapshot,
        IReadOnlyList<CapitalCashFlow> cashFlows,
        DateTimeOffset coverageFrom,
        CancellationToken cancellationToken
    );

    public Task RecordSizing(Guid orderId, RiskSizingSnapshot snapshot, CancellationToken cancellationToken);

    public Task RecordFills(Guid orderId, IReadOnlyList<RiskVerifiedFill> fills, CancellationToken cancellationToken);
}
