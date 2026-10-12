using Mediator;

namespace Quantara.MarketData.Application;

public sealed record GetMarketQuotesQuery : IQuery<IReadOnlyList<MarketQuoteResult>>;
