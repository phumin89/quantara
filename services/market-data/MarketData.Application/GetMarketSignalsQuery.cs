using Mediator;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Application;

public sealed record GetMarketSignalsQuery : IQuery<IntradaySnapshot>;
