using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveOwnerControlFlowTests(PostgresFixture database)
{
    [Fact]
    public async Task Negative_preflight_evidence_revokes_saved_authority_without_changing_money()
    {
        var (store, state, controls) = await Setup();
        Assert.True((await controls.ArmAsync(Request(state))).Allowed);
        var denied = new LiveOwnerControls(store, new Preflight(false, false),
            new LiveExecutionFlowTests.TestClock(state.Snapshot.ObservedAt), state.AccountId);
        var result = await denied.PreflightAsync();
        Assert.False(result.Ready);
        Assert.Contains("PERMISSION_VERIFICATION_REQUIRED", result.Violations);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(state.AccountId);
        Assert.False(restored.Gate.Armed);
        Assert.False(restored.Snapshot.Reconciled);
        Assert.Equal(state.Snapshot.CashBalance, restored.Snapshot.CashBalance);
        Assert.Empty(restored.Orders);
    }

    [Fact]
    public async Task Explicit_activation_and_stop_are_durable_and_do_not_create_orders()
    {
        var (store, state, controls) = await Setup();
        var result = await controls.ArmAsync(Request(state));
        Assert.True(result.Allowed);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(state.AccountId);
        Assert.True(restored.Gate.Armed);
        Assert.Equal("test-owner-account", restored.BrokerAccountReference);
        Assert.Empty(restored.Orders);
        await controls.StopAsync();
        restored = await store.ReadAccountAsync(state.AccountId);
        Assert.True(restored.Gate.Stopped);
        Assert.False(restored.Gate.Armed);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Contains(restored.Audit, a => a.Kind == "OwnerArmed");
        Assert.Contains(restored.Audit, a => a.Kind == "StopChanged");
    }

    [Fact]
    public async Task Local_owner_command_uses_current_verified_snapshot_while_browser_approval_stays_strict()
    {
        var (store, state, controls) = await Setup();
        var evidence = new LiveBrokerEvidence(state.AccountId, state.Mandate.CredentialFingerprint,
            state.Snapshot.ObservedAt, true, 1000m, 0m, [], [], [], state.Snapshot.Quotes);
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { Assert.True(tx.Reconcile(evidence, evidence, state.Snapshot.ObservedAt).Complete); await tx.CommitAsync(ct); return 0; });
        var strict = await controls.ArmAsync(Request(state));
        Assert.False(strict.Allowed);
        Assert.Contains("STALE_APPROVAL", strict.Violations);

        var reconciliation = new LiveReconciliation(store, new Source(Evidence(state)),
            new LiveExecutionFlowTests.TestClock(state.Snapshot.ObservedAt));

        var result = await controls.ArmCurrentSnapshotForOwnerCommandAsync(state.AccountId,
            "test-owner-account", state.Mandate.Version, reconciliation);

        Assert.True(result.Allowed, string.Join(",", result.Violations));
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.True(restored.Gate.Armed);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Equal(state.Mandate.Version, restored.Mandate.Version);
        Assert.Empty(restored.Orders);
    }

    [Theory]
    [InlineData("mandate")]
    [InlineData("stop")]
    [InlineData("binding")]
    public async Task Current_snapshot_owner_command_cannot_accept_changed_authority_or_clear_stop(string defect)
    {
        var (store, state, controls) = await Setup();
        if (defect == "mandate") Assert.True((await controls.SaveMandateAsync(state.Mandate with
            { Version = state.Mandate.Version + 1, MaximumOrderNotional = 300m })).Allowed);
        if (defect == "stop") await controls.StopAsync();

        var reconciliation = new LiveReconciliation(store, new Source(Evidence(state)),
            new LiveExecutionFlowTests.TestClock(state.Snapshot.ObservedAt));

        var result = await controls.ArmCurrentSnapshotForOwnerCommandAsync(state.AccountId,
            defect == "binding" ? "different-account" : "test-owner-account", state.Mandate.Version, reconciliation);

        Assert.False(result.Allowed);
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.False(restored.Gate.Armed);
        Assert.Equal(defect == "stop", restored.Gate.Stopped);
        Assert.Equal(defect == "mandate" ? 300m : state.Mandate.MaximumOrderNotional, restored.Mandate.MaximumOrderNotional);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Empty(restored.Orders);
    }

    [Fact]
    public async Task Current_snapshot_owner_command_reconciles_stale_unreconciled_snapshot_before_arm()
    {
        var (store, state, _) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.FailReconciliation("EARLIER_BACKGROUND_FAILURE", state.Snapshot.ObservedAt); await tx.CommitAsync(ct); return 0; });
        var now = state.Snapshot.ObservedAt.AddSeconds(60);
        var clock = new LiveExecutionFlowTests.TestClock(now);
        var controls = new LiveOwnerControls(store, new Preflight(false), clock, state.AccountId);
        var strict = await controls.ArmAsync(Request(await store.ReadAccountAsync(state.AccountId)));
        Assert.Contains("STALE_SNAPSHOT", strict.Violations);
        Assert.Contains("RECONCILIATION_REQUIRED", strict.Violations);
        var fresh = Evidence(state) with { ObservedAt = now,
            Quotes = state.Snapshot.Quotes.Select(q => q with { ObservedAt = now }).ToArray() };
        var reconciliation = new LiveReconciliation(store, new Source(fresh), clock);

        var result = await controls.ArmCurrentSnapshotForOwnerCommandAsync(state.AccountId,
            "test-owner-account", state.Mandate.Version, reconciliation);

        Assert.True(result.Allowed, string.Join(",", result.Violations));
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.True(restored.Snapshot.Reconciled);
        Assert.Equal(now, restored.Snapshot.ObservedAt);
        Assert.True(restored.Gate.Armed);
        Assert.Equal(state.Mandate.Version, restored.Mandate.Version);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Empty(restored.Orders);
    }

    [Fact]
    public async Task Owner_reconciliation_holds_lease_through_arm_before_queued_failure_can_disarm()
    {
        var (store, state, controls) = await Setup();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedFailure = new Source(Evidence(state), async () =>
        {
            await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
            {
                callbackEntered.TrySetResult(tx.Account.Gate.Armed);
                tx.FailReconciliation("QUEUED_FAILURE", state.Snapshot.ObservedAt);
                await tx.CommitAsync(ct);
                return 0;
            }, deadline.Token);
        });
        var reconciliation = new LiveReconciliation(store, queuedFailure,
            new LiveExecutionFlowTests.TestClock(state.Snapshot.ObservedAt));

        var result = await controls.ArmCurrentSnapshotForOwnerCommandAsync(state.AccountId,
            "test-owner-account", state.Mandate.Version, reconciliation, deadline.Token);
        await queuedFailure.Callback.WaitAsync(deadline.Token);

        Assert.True(result.Allowed);
        Assert.True(await callbackEntered.Task.WaitAsync(deadline.Token));
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.False(restored.Gate.Armed);
        Assert.False(restored.Snapshot.Reconciled);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Equal(state.Mandate.Version, restored.Mandate.Version);
        Assert.Empty(restored.Orders);
    }

    [Fact]
    public async Task Failed_owner_evidence_rejects_and_preserves_stop_money_mandate_and_orders()
    {
        var (store, state, controls) = await Setup();
        var before = await store.ReadAccountAsync(state.AccountId);
        var reconciliation = new LiveReconciliation(store, new Source(Evidence(state), fail: true),
            new LiveExecutionFlowTests.TestClock(state.Snapshot.ObservedAt));

        var result = await controls.ArmCurrentSnapshotForOwnerCommandAsync(state.AccountId,
            "test-owner-account", state.Mandate.Version, reconciliation);

        Assert.False(result.Allowed);
        Assert.Contains("BROKER_EVIDENCE_UNAVAILABLE", result.Violations);
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.Equal(before.Gate.Stopped, restored.Gate.Stopped);
        Assert.False(restored.Gate.Armed);
        Assert.Equal(before.Mandate.Version, restored.Mandate.Version);
        Assert.Equal(before.Mandate.MaximumOrderNotional, restored.Mandate.MaximumOrderNotional);
        Assert.Equal(before.Mandate.CredentialFingerprint, restored.Mandate.CredentialFingerprint);
        Assert.Equal(before.Snapshot.CashBalance, restored.Snapshot.CashBalance);
        Assert.Equal(before.Snapshot.CashReserved, restored.Snapshot.CashReserved);
        Assert.Equal(before.Orders, restored.Orders);
    }

    [Fact]
    public async Task Changing_mandate_disarms_and_stale_activation_cannot_restore_old_limits()
    {
        var (store, state, controls) = await Setup();
        Assert.True((await controls.ArmAsync(Request(state))).Allowed);
        Assert.True((await controls.SaveMandateAsync(state.Mandate with { Version = 2, MaximumOrderNotional = 300 })).Allowed);
        Assert.False((await controls.ArmAsync(Request(state))).Allowed);
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.False(restored.Gate.Armed);
        Assert.False(restored.Snapshot.Reconciled);
        Assert.Equal(2, restored.Mandate.Version);
        Assert.Equal(300m, restored.Mandate.MaximumOrderNotional);
    }

    [Fact]
    public async Task Preflight_failure_durably_disarms_without_echoing_private_details()
    {
        var (store, state, controls) = await Setup();
        Assert.True((await controls.ArmAsync(Request(state))).Allowed);
        var failed = new LiveOwnerControls(store, new Preflight(true), new LiveExecutionFlowTests.TestClock(state.Snapshot.ObservedAt), state.AccountId);
        var result = await failed.ArmAsync(Request(state));
        Assert.False(result.Allowed);
        Assert.Contains("PREFLIGHT_UNAVAILABLE", result.Violations);
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.False(restored.Gate.Armed);
        Assert.DoesNotContain(restored.Audit, a => a.Detail.Contains("private-secret"));
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
    }

    private async Task<(LiveTradingStore Store, LiveAccountState State, LiveOwnerControls Controls)> Setup()
    {
        var (store, template, _) = await new LiveTradingStoreTests(database).Setup();
        var id = new LiveAccountId(Guid.NewGuid());
        var state = template with
        {
            AccountId = id, Mandate = template.Mandate with { AccountId = id },
            Gate = template.Gate with { Armed = false }, Snapshot = template.Snapshot with { AccountId = id },
            PerformanceBaseline = new(new(2026, 9, 27), 1000, 1000)
        };
        await store.CreateAsync(state);
        return (store, state, new(store, new Preflight(false), new LiveExecutionFlowTests.TestClock(state.Snapshot.ObservedAt), id));
    }

    private static LiveArmRequest Request(LiveAccountState state)
        => new(state.AccountId, "test-owner-account", state.Mandate.Version, state.Snapshot.Version, true, false);

    private static LiveBrokerEvidence Evidence(LiveAccountState state)
        => new(state.AccountId, state.Mandate.CredentialFingerprint, state.Snapshot.ObservedAt, true,
            state.Snapshot.CashBalance, state.Snapshot.CashReserved, [], [], [], state.Snapshot.Quotes);

    private sealed class Source(LiveBrokerEvidence evidence, Func<Task>? callback = null, bool fail = false) : ILiveReconciliationSource
    {
        private bool started;
        public Task Callback { get; private set; } = Task.CompletedTask;

        public Task<LiveBrokerEvidence> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken)
        {
            if (!started) { started = true; Callback = callback?.Invoke() ?? Task.CompletedTask; }
            return fail ? throw new IOException("synthetic evidence failure") : Task.FromResult(evidence);
        }
    }

    private sealed class Preflight(bool fail, bool permissions = true) : ILiveActivationSource
    {
        public Task<LiveActivationEvidence> CheckAsync(LiveAccountState account, CancellationToken cancellationToken)
            => fail ? throw new IOException("private-secret") : Task.FromResult(new LiveActivationEvidence(account.AccountId,
                "test-owner-account", account.Mandate.CredentialFingerprint, account.Mandate.Version,
                account.Snapshot.Version, account.Snapshot.ObservedAt, true, true, permissions, true));
    }
}
