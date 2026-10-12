using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Application;

public interface IMarketDataReader
{
    public Task<IReadOnlyList<MarketCoin>> ReadCoins(CancellationToken cancellationToken);
    public Task<IReadOnlyList<MarketQuote>> ReadQuotes(CancellationToken cancellationToken);
}
