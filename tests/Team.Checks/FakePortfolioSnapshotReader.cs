using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakePortfolioSnapshotReader : IPortfolioSnapshotReader
{
    public Task Refresh(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> Read(CancellationToken cancellationToken) =>
        Task.FromResult("{\"positions\":[{\"symbol\":\"BTC\"}],\"summary\":{\"totalAssets\":100}}");
}
