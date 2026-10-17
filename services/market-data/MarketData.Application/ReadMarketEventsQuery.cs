using Mediator;

namespace Quantara.MarketData.Application;

public sealed record ReadMarketEventsQuery : IQuery<IAsyncEnumerable<MarketEvent>>;
