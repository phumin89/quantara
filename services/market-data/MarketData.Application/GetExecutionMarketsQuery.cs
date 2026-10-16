using Mediator;

namespace Quantara.MarketData.Application;

public sealed record GetExecutionMarketsQuery(IReadOnlyList<string> Symbols) : IQuery<string>;
