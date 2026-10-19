namespace Quantara.MarketData.Application;

public interface IExecutionMarketReader
{
    public Task<string> Read(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken,
        bool includeHistory = true,
        bool adaptiveTimeframes = false
    );

    public Task<string> ReadUniverse(CancellationToken cancellationToken);

    public Task<string> ReadHistorical(
        string symbol,
        int timeframeSeconds,
        long from,
        long until,
        CancellationToken cancellationToken
    );
}
