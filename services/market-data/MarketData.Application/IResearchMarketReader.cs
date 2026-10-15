namespace Quantara.MarketData.Application;

public interface IResearchMarketReader
{
    public Task<string> Read(CancellationToken cancellationToken, IReadOnlyList<string>? symbols = null);
}
