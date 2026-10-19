namespace Quantara.MarketData.Application.News;

public interface INewsReader
{
    public Task<NewsPacket> Read(string? symbols, DateTimeOffset? asOf, CancellationToken cancellationToken);
}
