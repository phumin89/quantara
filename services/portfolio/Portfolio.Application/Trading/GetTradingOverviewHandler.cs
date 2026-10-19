using Mediator;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed class GetTradingOverviewHandler(
    IReadOnlyTradingStore store,
    TradingOverviewProjection projection,
    IReadOnlyPortfolioGoalStore? goals = null,
    TimeProvider? time = null
) : IQueryHandler<GetTradingOverviewQuery, string>
{
    public async ValueTask<string> Handle(GetTradingOverviewQuery query, CancellationToken cancellationToken)
    {
        PortfolioGoalCheckpoint? checkpoint = null;
        if (goals is not null)
        {
            try
            {
                checkpoint = await goals.Read(
                    PortfolioObjective.Cutoff((time ?? TimeProvider.System).GetUtcNow()),
                    cancellationToken
                );
            }
            catch (System.Data.Common.DbException)
            {
                // Optional goal accounting cannot hide trading status or owner controls.
            }
        }

        return projection.Serialize(await store.Read(cancellationToken), checkpoint);
    }
}
