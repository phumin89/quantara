using System.Security.Cryptography;
using System.Text;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveExecutionFlowTests(PostgresFixture database)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_disarms_preserves_stop_and_releases_only_provably_unsent_orders(bool stopped)
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        var unsent = intent with { Quantity = .001m };
        LiveIntent uncertain = null!;
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(unsent, clock.Now);
            uncertain = intent with { Id = Guid.NewGuid(), Quantity = .003m, SnapshotVersion = tx.Account.Snapshot.Version };
            tx.Reserve(uncertain, clock.Now);
            tx.RecordTransition(uncertain.Id, LiveOrderState.Dispatching, null, clock.Now);
            if (stopped) tx.SetStopped(true);
            await tx.CommitAsync(ct);
            return 0;
        });
        var broker = new FakeBroker();
        var service = new LiveExecutionService(store, broker, clock);
        await service.SuspendForRecoveryAsync(state.AccountId);
        var recovered = await store.ReadAccountAsync(state.AccountId);
        Assert.False(recovered.Gate.Armed);
        Assert.Equal(stopped, recovered.Gate.Stopped);
        Assert.False(recovered.Snapshot.Reconciled);
        Assert.Equal(1000m, recovered.Snapshot.CashBalance);
        Assert.Equal(300.9m, recovered.Snapshot.CashReserved);
        Assert.Equal(LiveOrderState.Rejected, recovered.Orders.Single(o => o.Intent.Id == unsent.Id).State);
        Assert.Equal(LiveOrderState.Unknown, recovered.Orders.Single(o => o.Intent.Id == uncertain.Id).State);
        await service.SubmitAsync(uncertain);
        Assert.Equal(0, broker.PlacementCount);
        Assert.Equal(0, broker.CancellationCount);
    }

    [Fact]
    public async Task Cancel_after_stop_uses_only_saved_identity_and_keeps_exposure_until_reconciliation()
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        var broker = new FakeBroker();
        var service = new LiveExecutionService(store, broker, clock);
        await service.SubmitAsync(intent);
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.SetStopped(true); await tx.CommitAsync(ct); return 0; });
        broker.OnCancel = async (symbol, id, side) =>
        {
            Assert.Equal("BTC-THB", symbol);
            Assert.Equal("broker-1", id);
            Assert.Equal(OrderSide.Buy, side);
            var durable = await store.ReadAccountAsync(state.AccountId);
            Assert.NotNull(Assert.Single(durable.Orders).CancellationRequestedAt);
            Assert.False(durable.Snapshot.Reconciled);
            Assert.Equal(702.1m, durable.Snapshot.CashReserved);
            return new(BrokerSubmissionState.Accepted, "CANCEL_ACCEPTED");
        };
        var result = await service.CancelOwnedAsync(state.AccountId, intent.Id);
        Assert.Equal(BrokerSubmissionState.Accepted, result.CancellationOutcome);
        Assert.Equal(LiveOrderState.Acknowledged, result.State);
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
        Assert.True(restored.Gate.Stopped);
        Assert.False(restored.Gate.Armed);
        Assert.False(restored.Snapshot.Reconciled);
        Assert.Equal(1, broker.CancellationCount);
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.ApplyFill(new(state.AccountId, "racing-fill", "broker-1", OrderSide.Buy, "BTC-THB", .002m, 100000m, .5m, 1), clock.Now);
            await tx.CommitAsync(ct);
            return 0;
        });
        var afterRace = await store.ReadAsync(state.AccountId);
        Assert.Equal(799.5m, afterRace.CashBalance);
        Assert.Equal(501.6m, afterRace.CashReserved);
        Assert.Equal(.002m, Assert.Single(afterRace.Positions).Quantity);
        Assert.False(afterRace.Reconciled);
    }

    [Fact]
    public async Task Lost_cancel_reply_is_durable_unknown_and_never_automatically_repeated()
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        var broker = new FakeBroker { OnCancel = (_, _, _) => throw new HttpRequestException("reply lost") };
        var service = new LiveExecutionService(store, broker, clock);
        await service.SubmitAsync(intent);
        Assert.Equal(BrokerSubmissionState.Unknown, (await service.CancelOwnedAsync(state.AccountId, intent.Id)).CancellationOutcome);
        var restarted = new LiveExecutionService(new LiveTradingStore(database.ConnectionString), broker, clock);
        Assert.Equal(BrokerSubmissionState.Unknown, (await restarted.CancelOwnedAsync(state.AccountId, intent.Id)).CancellationOutcome);
        Assert.Equal(1, broker.CancellationCount);
        Assert.Equal(702.1m, (await store.ReadAsync(state.AccountId)).CashReserved);
    }

    [Fact]
    public async Task Restart_with_pending_cancellation_does_not_resend_or_release_reservation()
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        var broker = new FakeBroker();
        var service = new LiveExecutionService(store, broker, clock);
        await service.SubmitAsync(intent);
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.RequestCancellation(intent.Id, clock.Now); await tx.CommitAsync(ct); return 0; });
        await service.SuspendForRecoveryAsync(state.AccountId);
        var result = await service.CancelOwnedAsync(state.AccountId, intent.Id);
        Assert.Equal(BrokerSubmissionState.Unknown, result.CancellationOutcome);
        Assert.Equal(0, broker.CancellationCount);
        Assert.Equal(702.1m, result.ReservedCash);
    }

    [Fact]
    public async Task Unknown_local_intent_cannot_cancel_a_broker_order()
    {
        var (store, state, _) = await new LiveTradingStoreTests(database).Setup();
        var broker = new FakeBroker();
        await Assert.ThrowsAsync<InvalidDataException>(() => new LiveExecutionService(store, broker,
            new TestClock(state.Snapshot.ObservedAt)).CancelOwnedAsync(state.AccountId, Guid.NewGuid()));
        Assert.Equal(0, broker.CancellationCount);
        Assert.Empty((await store.ReadAccountAsync(state.AccountId)).Orders);
    }

    [Fact]
    public async Task Accepted_order_has_durable_exact_request_before_send_and_is_not_sent_twice_after_restart()
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        var broker = new FakeBroker();
        broker.OnPlace = async request =>
        {
            var stored = Assert.Single((await store.ReadAccountAsync(state.AccountId)).Orders);
            Assert.Equal(LiveOrderState.Dispatching, stored.State);
            Assert.Equal(request, stored.PreparedRequest);
            Assert.NotNull(stored.DispatchStartedAt);
            Assert.Equal(702.1m, stored.ReservedCash);
            return new(BrokerSubmissionState.Accepted, "broker-1", "ACCEPTED");
        };
        var order = await new LiveExecutionService(store, broker, clock).SubmitAsync(intent);
        Assert.Equal(LiveOrderState.Acknowledged, order.State);
        Assert.Equal("broker-1", order.BrokerId);
        var again = await new LiveExecutionService(new LiveTradingStore(database.ConnectionString), broker, clock).SubmitAsync(intent);
        Assert.Equal(order with { Violations = again.Violations }, again);
        Assert.Equal(order.Violations.ToArray(), again.Violations.ToArray());
        Assert.Equal(1, broker.PlacementCount);
        var snapshot = await store.ReadAsync(state.AccountId);
        Assert.Equal(1000m, snapshot.CashBalance); // Acceptance is not a fill.
        Assert.Equal(702.1m, snapshot.CashReserved);
        Assert.Empty(snapshot.Positions);
    }

    [Fact]
    public async Task Timeout_after_send_is_durable_unknown_and_never_retried()
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        var broker = new FakeBroker { OnPlace = _ => throw new HttpRequestException("simulated reply loss after acceptance") };
        var result = await new LiveExecutionService(store, broker, clock).SubmitAsync(intent);
        Assert.Equal(LiveOrderState.Unknown, result.State);
        Assert.Equal(LiveOrderState.Unknown, (await new LiveExecutionService(new LiveTradingStore(database.ConnectionString), broker, clock).SubmitAsync(intent)).State);
        Assert.Equal(1, broker.PlacementCount);
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.False(restored.Gate.Armed);
        Assert.False(restored.Snapshot.Reconciled);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
    }

    [Fact]
    public async Task Caller_cancellation_after_transmission_does_not_erase_unknown_outcome()
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        using var cancellation = new CancellationTokenSource();
        var broker = new FakeBroker { OnPlace = _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        } };
        var result = await new LiveExecutionService(store, broker, clock).SubmitAsync(intent, cancellation.Token);
        Assert.Equal(LiveOrderState.Unknown, result.State);
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.Equal(LiveOrderState.Unknown, Assert.Single(restored.Orders).State);
        Assert.False(restored.Gate.Armed);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
        Assert.Equal(1, broker.PlacementCount);
    }

    [Fact]
    public async Task Quote_becoming_stale_during_preparation_blocks_send_and_releases_unsent_reservation()
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        var broker = new FakeBroker { OnPrepare = () => clock.Now = clock.Now.AddSeconds(31) };
        var order = await new LiveExecutionService(store, broker, clock).SubmitAsync(intent);
        Assert.Equal(LiveOrderState.Rejected, order.State);
        Assert.Contains("STALE_SNAPSHOT", order.Violations);
        Assert.Equal(0, broker.PlacementCount);
        Assert.Equal(0, (await store.ReadAsync(state.AccountId)).CashReserved);
    }

    [Fact]
    public async Task Restart_with_durable_dispatching_is_unknown_even_if_process_died_before_send()
    {
        var (store, state, intent) = await new LiveTradingStoreTests(database).Setup();
        var clock = new TestClock(state.Snapshot.ObservedAt);
        var broker = new FakeBroker();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, clock.Now);
            await tx.CommitAsync(ct);
            tx.PrepareDispatch(intent.Id, await broker.PrepareAsync(intent, ct), clock.Now);
            await tx.CommitAsync(ct);
            return 0; // Process dies here, before calling the broker.
        });
        await store.WithAccountLockAsync(state.AccountId, (tx, _) =>
        {
            Assert.Throws<InvalidOperationException>(() => tx.ConfirmDispatch(intent.Id, clock.Now.AddSeconds(31)));
            Assert.Equal(LiveOrderState.Dispatching, tx.ReadOrder(intent.Id)!.State);
            return Task.FromResult(0);
        });
        var result = await new LiveExecutionService(new LiveTradingStore(database.ConnectionString), broker, clock).SubmitAsync(intent);
        Assert.Equal(LiveOrderState.Unknown, result.State);
        Assert.Equal(0, broker.PlacementCount);
        Assert.Equal(702.1m, result.ReservedCash);
    }

    internal sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class FakeBroker : ILiveBroker
    {
        public int PlacementCount { get; private set; }
        public int CancellationCount { get; private set; }
        public Func<string, string, OrderSide, Task<BrokerCancellation>>? OnCancel { get; set; }
        public Action? OnPrepare { get; init; }
        public Func<PreparedLiveOrder, Task<BrokerSubmission>>? OnPlace { get; set; }

        public Task<PreparedLiveOrder> PrepareAsync(LiveIntent intent, CancellationToken cancellationToken)
        {
            OnPrepare?.Invoke();
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                sym = intent.Symbol.Replace('-', '_').ToLowerInvariant(),
                amt = intent.Side == OrderSide.Buy ? intent.Quantity * intent.LimitPrice : intent.Quantity,
                rat = intent.LimitPrice, typ = "limit", client_id = intent.Id.ToString("N")
            });
            return Task.FromResult(new PreparedLiveOrder(intent, body, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))));
        }

        public async Task<BrokerSubmission> PlaceAsync(PreparedLiveOrder request, CancellationToken cancellationToken)
        {
            PlacementCount++;
            return OnPlace is null ? new(BrokerSubmissionState.Accepted, "broker-1", "ACCEPTED") : await OnPlace(request);
        }

        public async Task<BrokerCancellation> CancelAsync(string symbol, string orderId, OrderSide side, CancellationToken cancellationToken)
        {
            CancellationCount++;
            return OnCancel is null ? new(BrokerSubmissionState.Accepted, "CANCEL_ACCEPTED") : await OnCancel(symbol, orderId, side);
        }
    }
}
