using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class GetRiskOverviewHandler(IReadOnlyRiskStore store, IAccountContext account)
    : IQueryHandler<GetRiskOverviewQuery, RiskOverview>
{
    public async ValueTask<RiskOverview> Handle(GetRiskOverviewQuery query, CancellationToken cancellationToken)
    {
        var policy = await store.ReadPolicy(cancellationToken);
        var capital = await store.ReadCapital(cancellationToken);
        var performance = await store.ReadPerformance(cancellationToken);
        if (capital is not null && capital.AccountKey != account.AccountKey)
        {
            throw new InvalidDataException("Risk overview belongs to a different account.");
        }

        if (capital is null || performance?.To != capital.ObservedAt)
        {
            performance = null;
        }

        return new(
            account.AccountKey,
            policy.PolicyHash,
            policy.Policy,
            capital is null ? null : RiskCapitalView.From(capital),
            performance is null ? null : RiskPerformanceView.FromPerformance(performance),
            null
        );
    }
}
