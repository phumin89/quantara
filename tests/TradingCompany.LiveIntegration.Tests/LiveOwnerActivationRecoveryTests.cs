using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using TradingCompany.LiveExecution;
using System.Text.Json;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveOwnerActivationRecoveryTests(PostgresFixture database)
{
    [Fact]
    public async Task Explicit_owner_activation_recovers_rounded_sell_and_old_day_before_funding_update_without_new_order()
    {
        var f = await Setup();
        using var broker = f.Broker;
        var result = await f.Activation.ActivateFullSpotAsync(default, useFundedOrderCeiling: true);

        Assert.True(result.Activated, string.Join(",", result.Reasons));
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(f.Id);
        Assert.Equal(929.12m, restored.Snapshot.CashBalance);
        Assert.Empty(restored.Snapshot.Positions);
        Assert.Equal(LiveOrderState.Filled, Assert.Single(restored.Orders).State);
        Assert.Equal(-.002994472m, Assert.Single(restored.Orders).SellQuoteAdjustment);
        Assert.Equal(1, f.Handler.PlacementCount); // Only the earlier synthetic submission.
        Assert.Single(restored.Fills);
        Assert.True(restored.Gate.Armed);
        Assert.False(restored.Gate.Stopped);
        Assert.Equal(restored.Mandate.CapitalAllocation, restored.Mandate.MaximumOrderNotional);
        Assert.Equal(1000m, restored.PerformanceBaseline!.HighWaterEquity);
        Assert.Equal(929.12m, restored.PerformanceBaseline.DayStartEquity);
        Assert.Equal(DateOnly.FromDateTime(f.Clock.Now.ToOffset(TimeSpan.FromHours(7)).DateTime), restored.PerformanceBaseline.BangkokDate);
        Assert.True((await f.Activation.ActivateFullSpotAsync(default, useFundedOrderCeiling: true)).Activated);
        Assert.Equal(929.12m, (await f.Store.ReadAsync(f.Id)).CashBalance);
        Assert.Equal(1, f.Handler.PlacementCount);
    }

    [Theory]
    [InlineData("receipt-missing")]
    [InlineData("wallet-mismatch")]
    [InlineData("owner-stop")]
    public async Task Recovery_failure_does_not_expand_authority_or_arm_or_resubmit(string defect)
    {
        var f = await Setup(defect);
        using var broker = f.Broker;
        var before = await f.Store.ReadAccountAsync(f.Id);

        Assert.False((await f.Activation.ActivateFullSpotAsync(default, useFundedOrderCeiling: true)).Activated);

        var after = await f.Store.ReadAccountAsync(f.Id);
        Assert.Equal(JsonSerializer.Serialize(before.Mandate), JsonSerializer.Serialize(after.Mandate));
        Assert.Equal(before.Snapshot.CashBalance, after.Snapshot.CashBalance);
        Assert.Equal(JsonSerializer.Serialize(before.Orders), JsonSerializer.Serialize(after.Orders));
        Assert.Empty(after.Fills);
        Assert.Equal(before.PerformanceBaseline, after.PerformanceBaseline);
        Assert.Equal(before.Gate.Stopped, after.Gate.Stopped);
        Assert.False(after.Gate.Armed);
        Assert.Equal(1, f.Handler.PlacementCount);
    }

    private async Task<Fixture> Setup(string? defect = null)
    {
        const decimal quantity = .0000104m;
        const decimal price = 2800287.93m;
        var (store, template, original) = await new LiveTradingStoreTests(database).Setup();
        var yesterday = template.Snapshot.ObservedAt;
        var clock = new LiveExecutionFlowTests.TestClock(yesterday);
        var handler = new BitkubDynamicMarketFlowTests.NativeMarketHandler("BTC-THB", price, 2, 8, clock,
            OrderSide.Sell, defect == "wallet-mismatch" ? 901m : 900m)
            { SellFeeOverride = .08m, CreditUsed = .08m };
        var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var id = new LiveAccountId(Guid.NewGuid());
        var snapshot = template.Snapshot with { AccountId = id, CashBalance = 900m,
            Equity = 900m + quantity * price, Positions = [new("BTC-THB", quantity, 0, price)],
            Quotes = [new("BTC-THB", price, price, yesterday)] };
        await store.CreateAsync(template with { AccountId = id,
            Mandate = template.Mandate with { AccountId = id, CredentialFingerprint = broker.CredentialFingerprint,
                MaximumOrderNotional = 100m, FeeReserveRate = .0035m, AutomaticDailyRollover = true },
            Gate = template.Gate with { CredentialFingerprint = broker.CredentialFingerprint },
            Snapshot = snapshot, FirstActivatedAt = yesterday.AddMinutes(-1) });
        var rules = new BitkubVerifiedExecutionRules("synthetic-rounded-sell", new string('a', 64), .0035m,
            BitkubBuyHistoryAmount.AuthenticatedOrderReceipts,
            [new("BTC-THB", 1m, .00000001m, .01m, 10m, 0, 1m) { BuyBaseRounding = BitkubBuyBaseRounding.FloorToBaseStep }]);
        var adapter = new BitkubExecutionAdapter(broker, store, id, clock, rules, receiptStreamReady: () => true);
        var intent = original with { AccountId = id, Side = OrderSide.Sell, Quantity = quantity, LimitPrice = price };
        var submitted = await new LiveExecutionService(store, adapter, clock).SubmitAsync(intent);
        Assert.True(submitted.State == LiveOrderState.Acknowledged, string.Join(",", submitted.Violations));
        await store.WithAccountLockAsync(id, async (tx, ct) =>
        {
            if (defect == "owner-stop") tx.SetStopped(true);
            else tx.SuspendForRecovery(yesterday);
            await tx.CommitAsync(ct);
            return 0;
        });
        clock.Now = yesterday.AddDays(1);
        var journal = new BitkubOrderReceiptJournal(database.ConnectionString, id, broker.CredentialFingerprint);
        if (defect != "receipt-missing") await journal.AppendAsync(new("native-order", intent.Id.ToString("N"),
            intent.Symbol, OrderSide.Sell, "filled", price, quantity, quantity, 29.12m, .08m, .08m, 0m,
            clock.Now.ToUnixTimeMilliseconds()));
        var preflight = new Preflight(clock);
        var configured = new BitkubConfiguredAccount(id, new("test-owner-account", broker.CredentialFingerprint,
            "synthetic-rounded-sell", new string('a', 64), true, true, rules));
        var reconciliation = new LiveReconciliation(store, new BitkubReconciliationSource(broker, adapter, id, clock, journal), clock);
        var controls = new LiveOwnerControls(store, preflight, clock, id);
        var provisioner = new BitkubAccountProvisioner(store, broker, configured, preflight, clock);
        return new(store, id, broker, handler, clock,
            new(configured, store, broker, provisioner, reconciliation, controls, clock));
    }

    private sealed record Fixture(LiveTradingStore Store, LiveAccountId Id, BitkubLiveBroker Broker,
        BitkubDynamicMarketFlowTests.NativeMarketHandler Handler, LiveExecutionFlowTests.TestClock Clock,
        LiveOwnerActivation Activation);

    private sealed class Preflight(TimeProvider clock) : ILiveActivationSource
    {
        public Task<LiveActivationEvidence> CheckAsync(LiveAccountState account, CancellationToken token)
            => Task.FromResult(new LiveActivationEvidence(account.AccountId, "test-owner-account",
                account.Mandate.CredentialFingerprint, account.Mandate.Version, account.Snapshot.Version,
                clock.GetUtcNow(), true, true, true, true));
    }
}
