using Mediator;

namespace Quantara.MarketData.Application;

public sealed record GetMarketResearchQuery(IReadOnlyList<string>? Symbols = null) : IQuery<string>;
