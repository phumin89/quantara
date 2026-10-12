using Mediator;

namespace Quantara.MarketData.Application;

public sealed class GetMarketQuotesHandler(IMarketDataReader reader)
    : IQueryHandler<GetMarketQuotesQuery, IReadOnlyList<MarketQuoteResult>>
{
    public async ValueTask<IReadOnlyList<MarketQuoteResult>> Handle(
        GetMarketQuotesQuery query,
        CancellationToken cancellationToken
    )
    {
        var quotes = await reader.ReadQuotes(cancellationToken);
        return quotes.Select(quote => new MarketQuoteResult(quote.Symbol, quote.Price)).ToArray();
    }
}
