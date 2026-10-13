using Mediator;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Application;

public sealed class GetMarketSignalsHandler(IIntradayMarketReader reader)
    : IQueryHandler<GetMarketSignalsQuery, IntradaySnapshot>
{
    public ValueTask<IntradaySnapshot> Handle(GetMarketSignalsQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(reader.Read());
}
