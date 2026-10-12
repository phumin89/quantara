using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakePortfolioSnapshotReader : IPortfolioSnapshotReader
{
    public Task<string> Read(CancellationToken cancellationToken) =>
        Task.FromResult("{\"positions\":[{\"symbol\":\"BTC\"}],\"summary\":{\"totalAssets\":100}}");
}
