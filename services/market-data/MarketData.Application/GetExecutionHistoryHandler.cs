using Mediator;

namespace Quantara.MarketData.Application;

public sealed class GetExecutionHistoryHandler(IExecutionMarketReader reader)
    : IQueryHandler<GetExecutionHistoryQuery, string>
{
    public async ValueTask<string> Handle(GetExecutionHistoryQuery query, CancellationToken cancellationToken) =>
        await reader.ReadHistorical(query.Symbol, query.TimeframeSeconds, query.From, query.To, cancellationToken);
}
