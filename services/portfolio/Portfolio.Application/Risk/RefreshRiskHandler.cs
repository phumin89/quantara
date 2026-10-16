using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class RefreshRiskHandler(IRiskStore store, RiskCapitalReader reader, IMediator mediator)
    : ICommandHandler<RefreshRiskCommand, RiskOverview>
{
    public async ValueTask<RiskOverview> Handle(RefreshRiskCommand command, CancellationToken cancellationToken)
    {
        var policy = await store.ReadPolicy(cancellationToken);
        var result = await reader.Read(null, null, policy.Policy, cancellationToken);
        await store.Observe(
            result.Snapshot,
            result.Evidence.CashFlows,
            result.Evidence.CashFlowCoverageFrom,
            cancellationToken
        );
        return await mediator.Send(new GetRiskOverviewQuery(), cancellationToken);
    }
}
