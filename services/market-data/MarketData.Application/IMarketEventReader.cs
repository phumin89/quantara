namespace Quantara.MarketData.Application;

public interface IMarketEventReader
{
    public IAsyncEnumerable<MarketEvent> Read(CancellationToken cancellationToken);
}
