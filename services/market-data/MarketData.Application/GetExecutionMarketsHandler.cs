using Mediator;

namespace Quantara.MarketData.Application;

public sealed class GetExecutionMarketsHandler(IExecutionMarketReader reader)
    : IQueryHandler<GetExecutionMarketsQuery, string>
{
    public ValueTask<string> Handle(GetExecutionMarketsQuery query, CancellationToken cancellationToken) =>
        new(reader.Read(query.Symbols, cancellationToken, query.IncludeHistory));
}
