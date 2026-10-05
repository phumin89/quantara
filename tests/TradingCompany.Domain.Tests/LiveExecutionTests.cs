using System.Security.Cryptography;
using System.Text;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Freshness_is_checked_after_durable_dispatch_and_lease_verification(bool expireMandate)
    {
        var clock = new TestClock();
        var store = new TestStore();
        if (expireMandate)
            store.State = store.State with { Mandate = store.State.Mandate with { ExpiresAt = clock.Now.AddSeconds(1) } };
        store.OnLease = () => clock.Now = clock.Now.AddSeconds(expireMandate ? 2 : 31);
        var broker = new TestBroker();

        var result = await new LiveExecutionService(store, broker, clock).SubmitAsync(LiveRiskTests.Intent);

        Assert.Equal(LiveOrderState.Rejected, result.State);
        Assert.NotEmpty(result.Violations);
        Assert.Equal(0, broker.Calls);
        Assert.Equal(0, store.State.Snapshot.CashReserved);
        Assert.Equal(LiveOrderState.Rejected, Assert.Single(store.State.Orders).State);
    }

    [Fact]
    public async Task Malformed_submission_is_durable_unknown_and_not_retried()
    {
        var store = new TestStore();
        var broker = new TestBroker { Result = null! };
        var service = new LiveExecutionService(store, broker, new TestClock());

        var result = await service.SubmitAsync(LiveRiskTests.Intent);

        Assert.Equal(LiveOrderState.Unknown, result.State);
        Assert.Equal(LiveOrderState.Unknown, (await service.SubmitAsync(LiveRiskTests.Intent)).State);
        Assert.False(store.State.Gate.Armed);
        Assert.True(store.State.Snapshot.CashReserved > 0);
        Assert.Equal(1, broker.Calls);
    }

    [Fact]
    public async Task Lost_acknowledgement_commit_does_not_authorize_a_second_send()
    {
        var store = new TestStore { FailAcknowledgement = true };
        var broker = new TestBroker();
        var service = new LiveExecutionService(store, broker, new TestClock());

        await Assert.ThrowsAsync<IOException>(() => service.SubmitAsync(LiveRiskTests.Intent));
        Assert.Equal(LiveOrderState.Dispatching, Assert.Single(store.State.Orders).State);
        Assert.Equal(LiveOrderState.Unknown, (await service.SubmitAsync(LiveRiskTests.Intent)).State);
        Assert.Equal(1, broker.Calls);
        Assert.True(store.State.Snapshot.CashReserved > 0);
    }

    [Fact]
    public async Task Lost_account_lease_prevents_external_send()
    {
        var store = new TestStore { OnLease = () => throw new IOException("session lost") };
        var broker = new TestBroker();
        await Assert.ThrowsAsync<IOException>(() => new LiveExecutionService(store, broker, new TestClock()).SubmitAsync(LiveRiskTests.Intent));
        Assert.Equal(0, broker.Calls);
        Assert.Equal(LiveOrderState.Dispatching, Assert.Single(store.State.Orders).State);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = LiveRiskTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Fault injection only: real session ownership and atomic persistence are covered by PostgreSQL tests.
    private sealed class TestStore : ILiveTradingStore
    {
        public LiveAccountState State { get; set; } = new(LiveRiskTests.Account, LiveRiskTests.Mandate,
            LiveRiskTests.Gate, LiveRiskTests.Snapshot, [], [], []);
        public Action? OnLease { get; set; }
        public bool FailAcknowledgement { get; init; }
        public Task<LiveSnapshot> ReadAsync(LiveAccountId id, CancellationToken cancellationToken = default) => Task.FromResult(State.Snapshot);
        public Task<LiveAccountState> ReadAccountAsync(LiveAccountId id, CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task<T> WithAccountLockAsync<T>(LiveAccountId id, Func<ILiveAccountTransaction, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
            => action(new LiveAccountTransaction(State, (next, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (FailAcknowledgement && next.Orders.Any(o => o.State == LiveOrderState.Acknowledged)) throw new IOException("commit lost");
                State = next;
                return Task.CompletedTask;
            }, ct => { ct.ThrowIfCancellationRequested(); OnLease?.Invoke(); return Task.CompletedTask; }), cancellationToken);
    }

    private sealed class TestBroker : ILiveBroker
    {
        public Task<BrokerCancellation> CancelAsync(string symbol, string orderId, OrderSide side, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public int Calls { get; private set; }
        public BrokerSubmission Result { get; init; } = new(BrokerSubmissionState.Accepted, "test-order", "ACCEPTED");
        public Task<PreparedLiveOrder> PrepareAsync(LiveIntent intent, CancellationToken cancellationToken)
        {
            const string body = "test-only-wire-request";
            return Task.FromResult(new PreparedLiveOrder(intent, body, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))));
        }
        public Task<BrokerSubmission> PlaceAsync(PreparedLiveOrder request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }
}
