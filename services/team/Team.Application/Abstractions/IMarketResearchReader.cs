namespace Quantara.Team.Application;

public interface IMarketResearchReader
{
    public Task<string> Read(string accountKey, CancellationToken cancellationToken);
}
