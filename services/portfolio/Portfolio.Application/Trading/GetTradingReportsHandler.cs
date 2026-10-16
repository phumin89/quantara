using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class GetTradingReportsHandler(IReadOnlyTradingStore store)
    : IQueryHandler<GetTradingReportsQuery, string>
{
    public async ValueTask<string> Handle(GetTradingReportsQuery query, CancellationToken cancellationToken) =>
        TradingOverviewProjection.SerializeReports(await store.Read(cancellationToken));
}
