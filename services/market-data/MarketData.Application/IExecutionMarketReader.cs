namespace Quantara.MarketData.Application;

public interface IExecutionMarketReader
{
    public Task<string> Read(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken,
        bool includeHistory = true
    );

    public Task<string> ReadUniverse(CancellationToken cancellationToken);
}
