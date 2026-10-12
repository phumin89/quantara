using Quantara.Portfolio.Domain;
namespace Quantara.Portfolio.Application;

public interface IMarketDataClient
{
    public Task<Coin[]> GetCoins(CancellationToken cancellationToken);
    public Task<Quote[]> GetQuotes(CancellationToken cancellationToken);
}
