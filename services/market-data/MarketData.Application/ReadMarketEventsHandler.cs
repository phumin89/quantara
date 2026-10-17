using Mediator;

namespace Quantara.MarketData.Application;

public sealed class ReadMarketEventsHandler(IMarketEventReader reader)
    : IQueryHandler<ReadMarketEventsQuery, IAsyncEnumerable<MarketEvent>>
{
    public ValueTask<IAsyncEnumerable<MarketEvent>> Handle(
        ReadMarketEventsQuery query,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(reader.Read(cancellationToken));
}
