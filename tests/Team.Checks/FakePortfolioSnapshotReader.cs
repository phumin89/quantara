using System.Text.Json;
using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakePortfolioSnapshotReader : IPortfolioSnapshotReader
{
    public string? TradingMode { get; init; }

    public static DateTimeOffset FetchedAt { get; } = DateTimeOffset.UtcNow;

    public Task Refresh(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> Read(CancellationToken cancellationToken)
    {
        var snapshot =
            $"{{\"accountKey\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"walletEvidence\":{{\"accountKey\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"observedAt\":\"{DateTimeOffset.UtcNow:O}\",\"balances\":[{{\"symbol\":\"THB\",\"quantity\":1000,\"available\":700}},{{\"symbol\":\"BTC\",\"quantity\":1,\"available\":1}}],\"fee\":{{\"rate\":0.0025,\"status\":\"estimated\"}}}},\"positions\":[{{\"symbol\":\"BTC\",\"quantity\":1}}],\"summary\":{{\"totalAssets\":100,\"availableThb\":700}},\"marketResearch\":{{\"available\":true,\"fetchedAt\":\"{FetchedAt:O}\",\"scan\":{{\"symbols\":[\"BTC_THB\"],\"last\":[100],\"usable\":[true]}},\"markets\":[{{\"symbol\":\"BTC_THB\",\"last\":100,\"bid\":99.9,\"ask\":100.1}}]}}}}";
        if (this.TradingMode is not null)
        {
            snapshot = "{\"tradingMode\":" + JsonSerializer.Serialize(this.TradingMode) + "," + snapshot[1..];
        }

        return Task.FromResult(snapshot);
    }
}
