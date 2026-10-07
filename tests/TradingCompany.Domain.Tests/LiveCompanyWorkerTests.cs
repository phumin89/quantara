using TradingCompany.Api;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveCompanyWorkerTests
{
    [Fact]
    public async Task Unexpected_journal_failure_does_not_kill_later_ticks_and_shutdown_cancels_the_wait()
    {
        var clock = new Clock(); var journal = new BrokenJournal(); var client = new Client();
        var coordinator = new LiveCompanyCoordinator(client, new Market(), new Meeting(), journal, clock, LiveRiskTests.Account);
        using var activity = new LiveCompanyActivityPublisher(client, clock, LiveRiskTests.Account);
        using var worker = new LiveCompanyWorker(coordinator, clock, activity);
        await worker.StartAsync(default);
        var timer = await clock.Created.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await journal.Failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        timer.Fire();
        await journal.LaterTick.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        Assert.Equal("LIVE_UNAVAILABLE", coordinator.Status);
        Assert.Equal(new LiveMeetingIssue("MEETING_FAILED"), Assert.Single(coordinator.Issues));
        Assert.False(client.Submitted);
    }

    private static TradingAccountContext Scope => new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N"));
    private sealed class BrokenJournal : IDailyMeetingJournal
    {
        private int failures;
        public TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LaterTick { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TradingAccountContext AccountContext => Scope;
        public void EnsureReconciled() { }
        public DailyMeetingAttempt? Read(DateOnly day) => null;
        public IReadOnlyList<DailyMeetingAttempt> ReadAll() { if (failures >= 2) LaterTick.TrySetResult(); return []; }
        public bool TryBegin(DateOnly day, Guid id) => true;
        public void Complete(DateOnly day, Guid id, string status, DailyMeetingSummary? summary = null)
        { if (++failures >= 2) Failed.TrySetResult(); throw new IOException("fixture journal write failure"); }
    }
    private sealed class Meeting : IAiCompanyMeetingRunner
    {
        public bool Enabled => true;
        public string Model => "fixture";
        public Task<AiCompanyMeetingResult> RunAsync(AiCompanyMeetingInput input, CancellationToken token) =>
            Task.FromResult(new AiCompanyMeetingResult("COMPLETED", [], [], 0, 0, 0, 0, Model) { AccountContext = Scope });
    }
    private sealed class Client : ILiveCompanyClient
    {
        public bool Submitted { get; private set; }
        public Task<LiveResearchPacket> ReadAsync(LiveAccountId account, CancellationToken token) => Task.FromResult(
            new LiveResearchPacket(Scope, 1, LiveRiskTests.Snapshot,
                new(1000m, .2m, 3, .003m, .01m, ["BTC-THB"]) { OrderSizing = [new("BTC-THB", .01m, .00000001m, 1m, 10m)] }, true, false));
        public Task SaveMeetingAsync(DailyMeetingAttempt attempt, CancellationToken token) => Task.CompletedTask;
        public Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken token)
        { Submitted = true; throw new InvalidOperationException("No fixture order may be sent."); }
    }
    private sealed class Market : IHistoricalMarketEvidenceProvider
    {
        public Task<IReadOnlyList<HistoricalBar>> GetAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken token) => throw new NotSupportedException();
        public Task<HistoricalMarketEvidence> GetEvidenceAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken token)
        {
            var bars = Enumerable.Range(0, 250).Select(i => new HistoricalBar(end.AddDays(i - 249), symbol, 75000 + i, 76000 + i, 74000 + i, 75500 + i, 1000)).ToArray();
            return Task.FromResult(new HistoricalMarketEvidence(bars, bars, [], "bitkub-daily", "THB"));
        }
    }
    private sealed class Clock : TimeProvider
    {
        public TaskCompletionSource<ManualTimer> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => LiveRiskTests.Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new ManualTimer(callback, state); Created.SetResult(timer); return timer; }
    }
    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
