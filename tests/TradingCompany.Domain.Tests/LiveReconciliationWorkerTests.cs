using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveReconciliationWorkerTests
{
    [Fact]
    public async Task Startup_recovers_before_reads_and_reconciles_every_thirty_seconds_even_while_stopped()
    {
        var store = new Store();
        var clock = new Clock();
        var source = new Source(store, clock);
        using var worker = new LiveReconciliationWorker(new(store, new NoPlacement(), clock), new(store, source, clock),
            LiveRiskTests.Account, clock);
        await worker.StartAsync(default);
        await store.WaitForCommits(2);
        await clock.WaitForTimer();
        Assert.False(store.Account.Gate.Armed);
        Assert.True(store.Account.Gate.Stopped);
        Assert.True(store.Account.Snapshot.Reconciled);
        Assert.Equal(2, source.Reads);
        Assert.Equal("RecoverySuspended", store.Account.Audit[0].Kind);
        clock.Advance(29);
        Assert.Equal(2, source.Reads);
        clock.Advance(1);
        await store.WaitForCommits(3);
        await clock.WaitForTimer();
        Assert.Equal(4, source.Reads);
        Assert.Equal(clock.GetUtcNow(), store.Account.Snapshot.ObservedAt);
        clock.Advance(30);
        await store.WaitForCommits(4);
        Assert.Equal(6, source.Reads);
        await worker.StopAsync(default);
        clock.Advance(30);
        Assert.Equal(6, source.Reads);
        Assert.Empty(store.Account.Orders);
    }

    [Fact]
    public async Task Failed_recovery_commit_prevents_any_broker_read_and_fails_host_startup()
    {
        var store = new Store { FailCommit = true };
        var clock = new Clock();
        var source = new Source(store, clock);
        using var worker = new LiveReconciliationWorker(new(store, new NoPlacement(), clock), new(store, source, clock),
            LiveRiskTests.Account, clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.StartAsync(default));
        Assert.Equal(0, source.Reads);
        Assert.Equal("RECOVERY_FAILED", worker.Status);
    }

    [Fact]
    public async Task Missing_runtime_services_or_account_never_reads_a_private_broker()
    {
        var store = new Store();
        var clock = new Clock();
        var source = new Source(store, clock);
        foreach (var worker in new[]
        {
            new LiveReconciliationWorker(null, new(store, source, clock), LiveRiskTests.Account, clock),
            new LiveReconciliationWorker(new(store, new NoPlacement(), clock), null, LiveRiskTests.Account, clock),
            new LiveReconciliationWorker(new(store, new NoPlacement(), clock), new(store, source, clock), default, clock)
        })
        {
            using (worker)
            {
                await worker.StartAsync(default);
                await worker.StopAsync(default);
                Assert.Equal("DISABLED", worker.Status);
            }
        }
        Assert.Equal(0, source.Reads);
        Assert.Empty(store.Account.Audit);
    }

    [Fact]
    public async Task Unavailable_evidence_disarms_without_spending_or_replacing_balances()
    {
        var store = new Store();
        var clock = new Clock();
        var source = new Source(store, clock) { Unavailable = true };
        using var worker = new LiveReconciliationWorker(new(store, new NoPlacement(), clock), new(store, source, clock),
            LiveRiskTests.Account, clock);
        await worker.StartAsync(default);
        await store.WaitForCommits(2);
        Assert.False(store.Account.Gate.Armed);
        Assert.False(store.Account.Snapshot.Reconciled);
        Assert.Equal(1000m, store.Account.Snapshot.CashBalance);
        Assert.Contains(store.Account.Audit, e => e.Kind == "ReconciliationBlocked");
        Assert.Empty(store.Account.Orders);
        await worker.StopAsync(default);
    }

    private sealed class Store : ILiveTradingStore
    {
        private readonly object sync = new();
        private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int commits;
        public bool FailCommit { get; init; }
        public LiveAccountState Account { get; private set; } = new(LiveRiskTests.Account, LiveRiskTests.Mandate,
            LiveRiskTests.Gate with { Stopped = true }, LiveRiskTests.Snapshot, [], [], [])
            { PerformanceBaseline = new(new(2026, 9, 27), 1000, 1000) };
        public Task<LiveAccountState> ReadAccountAsync(LiveAccountId id, CancellationToken ct) => Task.FromResult(Account);
        public Task<LiveSnapshot> ReadAsync(LiveAccountId id, CancellationToken ct) => Task.FromResult(Account.Snapshot);
        public Task<T> WithAccountLockAsync<T>(LiveAccountId id, Func<ILiveAccountTransaction, CancellationToken, Task<T>> action, CancellationToken ct)
            => action(new LiveAccountTransaction(Account, (state, _) =>
            {
                if (FailCommit) throw new IOException("fixture database unavailable");
                lock (sync) { Account = state; commits++; changed.TrySetResult(); changed = new(TaskCreationOptions.RunContinuationsAsynchronously); }
                return Task.CompletedTask;
            }), ct);
        public async Task WaitForCommits(int count)
        {
            while (true)
            {
                Task signal;
                lock (sync) { if (commits >= count) return; signal = changed.Task; }
                await signal.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
    }
    private sealed class Source(Store store, Clock clock) : ILiveReconciliationSource
    {
        public bool Unavailable { get; init; }
        public int Reads { get; private set; }
        public Task<LiveBrokerEvidence> ReadAsync(LiveAccountId id, CancellationToken ct)
        {
            Assert.False(store.Account.Gate.Armed);
            Assert.Contains(store.Account.Audit, e => e.Kind == "RecoverySuspended");
            Reads++;
            if (Unavailable) throw new IOException("fixture provider unavailable");
            return Task.FromResult(new LiveBrokerEvidence(id, "key-fingerprint", clock.GetUtcNow(), true, 1000, 0,
                [], [], [], [new("BTC-THB", 99900, 100000, clock.GetUtcNow())]));
        }
    }
    private sealed class NoPlacement : ILiveBroker
    {
        public Task<PreparedLiveOrder> PrepareAsync(LiveIntent intent, CancellationToken ct) => throw new Xunit.Sdk.XunitException("Reconciliation cannot prepare trades.");
        public Task<BrokerSubmission> PlaceAsync(PreparedLiveOrder request, CancellationToken ct) => throw new Xunit.Sdk.XunitException("Reconciliation cannot send trades.");
        public Task<BrokerCancellation> CancelAsync(string symbol, string orderId, OrderSide side, CancellationToken ct) => throw new Xunit.Sdk.XunitException("Recovery cannot cancel trades.");
    }
    private sealed class Clock : TimeProvider
    {
        private readonly object sync = new();
        private TaskCompletionSource scheduled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Timer? timer;
        private DateTimeOffset now = LiveRiskTests.Now;
        public override DateTimeOffset GetUtcNow() { lock (sync) return now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (sync) { timer = new(callback, state, now + dueTime, period); scheduled.TrySetResult(); return timer; }
        }
        public async Task WaitForTimer() { Task signal; lock (sync) signal = scheduled.Task; await signal.WaitAsync(TimeSpan.FromSeconds(3)); }
        public void Advance(int seconds)
        {
            Timer? due;
            lock (sync)
            {
                now = now.AddSeconds(seconds);
                due = timer is { Disposed: false } current && current.Due <= now ? current : null;
                if (due is not null) due.AdvanceDue();
            }
            due?.Fire();
        }
        private sealed class Timer(TimerCallback callback, object? state, DateTimeOffset due, TimeSpan period) : ITimer
        {
            public DateTimeOffset Due { get; private set; } = due;
            public bool Disposed { get; private set; }
            public void AdvanceDue() => Due = period > TimeSpan.Zero ? Due + period : DateTimeOffset.MaxValue;
            public void Fire() { if (!Disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
