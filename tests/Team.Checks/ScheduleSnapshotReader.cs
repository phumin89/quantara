using Quantara.Team.Application;

namespace Team.Checks;

public sealed class ScheduleSnapshotReader : IPortfolioSnapshotReader
{
    private readonly ManualTimeProvider time;

    public ScheduleSnapshotReader(ManualTimeProvider time) => this.time = time;

    public Task<string> Read(CancellationToken cancellationToken) => Task.FromResult(this.Snapshot());

    public Task<string> ReadForMarkets(IReadOnlyList<string> symbols, CancellationToken cancellationToken) =>
        Task.FromResult(this.Snapshot());

    public Task Refresh(CancellationToken cancellationToken) => Task.CompletedTask;

    private string Snapshot() =>
        $"{{\"accountKey\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"walletEvidence\":{{\"accountKey\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"observedAt\":\"{this.time.GetUtcNow():O}\",\"balances\":[{{\"symbol\":\"THB\",\"quantity\":1000,\"available\":700}},{{\"symbol\":\"BTC\",\"quantity\":1,\"available\":1}}]}},\"positions\":[{{\"symbol\":\"BTC\",\"quantity\":1}}],\"summary\":{{\"availableThb\":700}},\"marketResearch\":{{\"fetchedAt\":\"{this.time.GetUtcNow():O}\",\"historicalCandles\":{{\"candles\":[[1,2,3,4,5]]}},\"scan\":{{\"symbols\":[\"BTC_THB\"],\"last\":[100],\"usable\":[true]}},\"markets\":[{{\"symbol\":\"BTC_THB\",\"last\":100,\"bid\":99.9,\"ask\":100.1}}]}},\"tradingPerformance\":{{\"evaluation\":{{\"metrics\":[{{\"insufficientSamples\":true}}]}}}},\"tradingMode\":\"research\"}}";
}
