using Mediator;

namespace Quantara.MarketData.Application;

public sealed class GetMarketResearchHandler(IResearchMarketReader reader)
    : IQueryHandler<GetMarketResearchQuery, string>
{
    public ValueTask<string> Handle(GetMarketResearchQuery query, CancellationToken cancellationToken) =>
        new(reader.Read(cancellationToken));
}
