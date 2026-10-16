using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class GetTradingOverviewHandler(IReadOnlyTradingStore store, TradingOverviewProjection projection)
    : IQueryHandler<GetTradingOverviewQuery, string>
{
    public async ValueTask<string> Handle(GetTradingOverviewQuery query, CancellationToken cancellationToken) =>
        projection.Serialize(await store.Read(cancellationToken));
}
