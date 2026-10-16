using Mediator;

namespace Quantara.MarketData.Application;

public sealed class GetExecutionUniverseHandler(IExecutionMarketReader reader)
    : IQueryHandler<GetExecutionUniverseQuery, string>
{
    public ValueTask<string> Handle(GetExecutionUniverseQuery query, CancellationToken cancellationToken) =>
        new(reader.ReadUniverse(cancellationToken));
}
