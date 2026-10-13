namespace Quantara.Team.Application;

public interface IPortfolioSnapshotReader
{
    public Task<string> Read(CancellationToken cancellationToken);

    public Task Refresh(CancellationToken cancellationToken);

    public async Task EnsureReady(CancellationToken cancellationToken) => await this.Read(cancellationToken);
}
