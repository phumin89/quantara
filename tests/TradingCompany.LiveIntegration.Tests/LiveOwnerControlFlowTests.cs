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

    private sealed class Preflight(bool fail, bool permissions = true) : ILiveActivationSource
    {
        public Task<LiveActivationEvidence> CheckAsync(LiveAccountState account, CancellationToken cancellationToken)
            => fail ? throw new IOException("private-secret") : Task.FromResult(new LiveActivationEvidence(account.AccountId,
                "test-owner-account", account.Mandate.CredentialFingerprint, account.Mandate.Version,
                account.Snapshot.Version, account.Snapshot.ObservedAt, true, true, permissions, true));
    }
}
