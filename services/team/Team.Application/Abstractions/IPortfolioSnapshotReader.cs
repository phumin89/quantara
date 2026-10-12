namespace Quantara.Team.Application;

public interface IPortfolioSnapshotReader
{
    public Task<string> Read(CancellationToken cancellationToken);
}
