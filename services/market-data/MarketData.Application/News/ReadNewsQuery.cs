using Mediator;

namespace Quantara.MarketData.Application.News;

public sealed record ReadNewsQuery(string? Symbols, DateTimeOffset? AsOf) : IQuery<NewsPacket>;
