using Mediator;

namespace Quantara.MarketData.Application;

public sealed record GetMarketCoinsQuery : IQuery<IReadOnlyList<MarketCoinResult>>;
