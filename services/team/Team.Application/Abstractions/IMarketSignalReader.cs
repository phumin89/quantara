namespace Quantara.Team.Application;

public interface IMarketSignalReader
{
    public Task<MarketSignalNotice?> Read(CancellationToken cancellationToken);
}
