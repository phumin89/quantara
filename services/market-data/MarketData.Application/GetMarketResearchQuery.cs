using Mediator;

namespace Quantara.MarketData.Application;

public sealed record GetMarketResearchQuery() : IQuery<string>;
