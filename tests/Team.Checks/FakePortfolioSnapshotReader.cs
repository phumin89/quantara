using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakePortfolioSnapshotReader : IPortfolioSnapshotReader
{
    public static DateTimeOffset FetchedAt { get; } = DateTimeOffset.UtcNow;

    public Task Refresh(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> Read(CancellationToken cancellationToken) =>
        Task.FromResult(
            $"{{\"accountKey\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"positions\":[{{\"symbol\":\"BTC\",\"quantity\":1}}],\"summary\":{{\"totalAssets\":100,\"availableThb\":200}},\"marketResearch\":{{\"available\":true,\"fetchedAt\":\"{FetchedAt:O}\",\"markets\":[{{\"symbol\":\"BTC_THB\",\"last\":100,\"bid\":99.9,\"ask\":100.1}}]}}}}"
        );
}
