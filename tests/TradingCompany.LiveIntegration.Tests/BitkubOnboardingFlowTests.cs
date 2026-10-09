using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class BitkubOnboardingFlowTests(PostgresFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 5, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_small_order_ceiling_is_rejected_before_private_calls(decimal ceiling)
    {
        using var fixture = await Setup();

        var result = await fixture.Activation.ActivateFullSpotAsync(default, ceiling);

        Assert.False(result.Activated);
        Assert.Contains("MAXIMUM_ORDER_NOTIONAL_REQUIRED", result.Reasons);
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
        Assert.Empty(fixture.Handler.Paths);
    }

    [Fact]
    public async Task Small_order_activation_caps_new_account_and_is_idempotent()
    {
        using var fixture = await Setup(multiMarkets: true);

        var first = await fixture.Activation.ActivateFullSpotAsync(default, 100m);
        Assert.True(first.Activated, string.Join(",", first.Reasons));
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(100m, account.Mandate.MaximumOrderNotional);
        Assert.Equal(1000m, account.Mandate.CapitalAllocation);
        Assert.Equal(["BTC-THB", "ETH-THB", "SOL-THB"], account.Mandate.AllowedSymbols);
        Assert.Empty(account.Orders);
        var version = account.Mandate.Version;

        var second = await fixture.Activation.ActivateFullSpotAsync(default, 100m);

        Assert.True(second.Activated, string.Join(",", second.Reasons));
        Assert.Equal(version, (await fixture.Store.ReadAccountAsync(fixture.Id)).Mandate.Version);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Existing_lower_ceiling_cannot_be_increased_by_small_order_request()
    {
        using var fixture = await Setup();
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default, 20m)).Activated);
        var before = await fixture.Store.ReadAccountAsync(fixture.Id);

        var result = await fixture.Activation.ActivateFullSpotAsync(default, 100m);

        Assert.True(result.Activated, string.Join(",", result.Reasons));
        var after = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(20m, after.Mandate.MaximumOrderNotional);
        Assert.Equal(before.Mandate.Version, after.Mandate.Version);
        Assert.Equal(before.Gate, after.Gate);
        Assert.Empty(after.Orders);
    }

    [Fact]
    public async Task Explicit_funded_activation_restores_order_ceiling_and_verified_position_slots_without_resetting_account()
    {
        using var fixture = await Setup(multiMarkets: true);
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default, 100m)).Activated);
        var before = await fixture.Store.ReadAccountAsync(fixture.Id);
        var result = await fixture.Activation.ActivateFullSpotAsync(default, useFundedOrderCeiling: true);
        Assert.True(result.Activated, string.Join(",", result.Reasons));
        var after = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(before.Mandate.CapitalAllocation, after.Mandate.MaximumOrderNotional);
        Assert.Equal(3, after.Mandate.MaximumOpenPositions);
        Assert.Equal(before.Mandate.Version + 1, after.Mandate.Version);
        Assert.Equal(JsonSerializer.Serialize(before.Mandate), JsonSerializer.Serialize(after.Mandate with
        { Version = before.Mandate.Version, MaximumOrderNotional = before.Mandate.MaximumOrderNotional,
            MaximumOpenPositions = before.Mandate.MaximumOpenPositions }));
        Assert.Equal(before.PerformanceBaseline, after.PerformanceBaseline);
        Assert.Equal(before.FirstActivatedAt, after.FirstActivatedAt);
        Assert.Equal(before.Snapshot.CashBalance, after.Snapshot.CashBalance);
        Assert.Equal(before.Snapshot.DailyLoss, after.Snapshot.DailyLoss);
        Assert.True(after.Gate.Armed);
        Assert.Empty(after.Orders);
        Assert.Equal(0, fixture.Handler.OrderWrites);
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default, useFundedOrderCeiling: true)).Activated);
        Assert.Equal(after.Mandate.Version, (await fixture.Store.ReadAccountAsync(fixture.Id)).Mandate.Version);
    }

    [Fact]
    public async Task Funded_activation_upgrades_position_slots_when_order_ceiling_is_already_full()
    {
        using var fixture = await Setup(multiMarkets: true);
        Assert.True((await fixture.Provisioner.ProvisionAsync(fixture.Request, default)).Allowed);
        var before = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(1, before.Mandate.MaximumOpenPositions);
        Assert.True((await fixture.Services.GetRequiredService<LiveOwnerControls>().SaveMandateAsync(
            before.Mandate with { Version = before.Mandate.Version + 1,
                MaximumOrderNotional = before.Mandate.CapitalAllocation }, default)).Allowed);
        before = await fixture.Store.ReadAccountAsync(fixture.Id);

        var result = await fixture.Activation.ActivateFullSpotAsync(default, useFundedOrderCeiling: true);

        Assert.True(result.Activated, string.Join(",", result.Reasons));
        var after = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(before.Mandate.MaximumOrderNotional, after.Mandate.MaximumOrderNotional);
        Assert.Equal(3, after.Mandate.MaximumOpenPositions);
        Assert.Equal(before.Mandate.Version + 1, after.Mandate.Version);
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default, useFundedOrderCeiling: true)).Activated);
        Assert.Equal(after.Mandate.Version, (await fixture.Store.ReadAccountAsync(fixture.Id)).Mandate.Version);
    }

    [Fact]
    public async Task Conflicting_funded_and_small_order_requests_are_rejected_before_private_reads()
    {
        using var fixture = await Setup();
        var result = await fixture.Activation.ActivateFullSpotAsync(default, 100m, useFundedOrderCeiling: true);
        Assert.False(result.Activated);
        Assert.Contains("CONFLICTING_ORDER_CEILINGS", result.Reasons);
        Assert.Empty(fixture.Handler.Paths);
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
    }

    [Fact]
    public async Task Owner_activation_provisions_and_arms_funded_empty_account_without_orders()
    {
        using var fixture = await Setup();
        var result = await fixture.Activation.ActivateFullSpotAsync(default);
        Assert.True(result.Activated, string.Join(",", result.Reasons));
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(1000m, account.Mandate.CapitalAllocation);
        Assert.Equal(1000m, account.Mandate.MaximumOrderNotional);
        Assert.Equal(1000m, account.Mandate.MaximumDailyTurnover);
        Assert.Equal(1000m, account.Mandate.MaximumDailyLoss);
        Assert.Equal(1m, account.Mandate.MaximumDrawdown);
        Assert.Equal(1m, account.Mandate.MaximumPortfolioExposure);
        Assert.True(account.Mandate.AutomaticDailyRollover);
        Assert.NotNull(account.PerformanceBaseline);
        Assert.True(account.Snapshot.Reconciled);
        Assert.True(account.Gate.Armed);
        Assert.Empty(account.Orders);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Existing_BTC_account_expands_to_all_verified_spot_markets_without_resetting_state()
    {
        using var fixture = await Setup(multiMarkets: true);
        Assert.True((await fixture.Provisioner.ProvisionAsync(fixture.Request, default)).Allowed);
        var before = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.True((await fixture.Services.GetRequiredService<LiveReconciliation>().InitializeBaselineAsync(
            new(fixture.Id, before.Mandate.Version, before.Snapshot.Version), default)).Complete);
        before = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default)).Activated);
        var expanded = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(before.Mandate.Version + 1, expanded.Mandate.Version);
        Assert.Equal(new[] { "BTC-THB", "ETH-THB", "SOL-THB" }, expanded.Mandate.AllowedSymbols);
        Assert.Equal(before.Mandate.MaximumOpenPositions, expanded.Mandate.MaximumOpenPositions);
        Assert.Equal(JsonSerializer.Serialize(before.Mandate), JsonSerializer.Serialize(expanded.Mandate with
        { Version = before.Mandate.Version, AllowedSymbols = before.Mandate.AllowedSymbols }));
        Assert.Equal(before.Mandate.CapitalAllocation, expanded.Mandate.CapitalAllocation);
        Assert.Equal(before.Snapshot.CashBalance, expanded.Snapshot.CashBalance);
        Assert.Equal(JsonSerializer.Serialize(before.OpeningBalance), JsonSerializer.Serialize(expanded.OpeningBalance));
        Assert.Equal(before.PerformanceBaseline, expanded.PerformanceBaseline);
        Assert.True(expanded.Gate.Armed);
        Assert.Equal(0, fixture.Handler.OrderWrites);
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default)).Activated);
        Assert.Equal(expanded.Mandate.Version, (await fixture.Store.ReadAccountAsync(fixture.Id)).Mandate.Version);
    }

    [Fact]
    public async Task Older_profile_cannot_silently_remove_markets_from_an_existing_mandate()
    {
        using var fixture = await Setup(multiMarkets: true);
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default)).Activated);
        var before = await fixture.Store.ReadAccountAsync(fixture.Id);
        var configured = fixture.Services.GetRequiredService<BitkubConfiguredAccount>();
        var narrower = configured with { Verification = configured.Verification with
        { Rules = configured.Verification.Rules with { Pairs = configured.Verification.Rules.Pairs.Where(p => p.Symbol == "BTC-THB").ToArray() } } };
        var activation = new LiveOwnerActivation(narrower, fixture.Store,
            fixture.Services.GetRequiredService<BitkubLiveBroker>(), fixture.Provisioner,
            fixture.Services.GetRequiredService<LiveReconciliation>(), fixture.Services.GetRequiredService<LiveOwnerControls>(),
            fixture.Services.GetRequiredService<TimeProvider>());

        var result = await activation.ActivateFullSpotAsync(default);

        Assert.False(result.Activated);
        Assert.Contains("MARKET_REMOVAL_REQUIRES_MANDATE_UPDATE", result.Reasons);
        var after = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(JsonSerializer.Serialize(before.Mandate), JsonSerializer.Serialize(after.Mandate));
        Assert.Equal(before.Gate, after.Gate);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Stopped_BTC_account_cannot_expand_verified_spot_markets(bool smallOrders, bool fundedOrders)
    {
        using var fixture = await Setup(multiMarkets: true);
        Assert.True((await fixture.Provisioner.ProvisionAsync(fixture.Request, default)).Allowed);
        await fixture.Services.GetRequiredService<LiveOwnerControls>().StopAsync();
        var before = await fixture.Store.ReadAccountAsync(fixture.Id);
        var result = await fixture.Activation.ActivateFullSpotAsync(default, smallOrders ? 100m : null, fundedOrders);
        Assert.False(result.Activated);
        Assert.Contains("OWNER_STOP_ACTIVE", result.Reasons);
        var after = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(JsonSerializer.Serialize(before.Mandate), JsonSerializer.Serialize(after.Mandate));
        Assert.True(after.Gate.Stopped);
        Assert.False(after.Gate.Armed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Pending_owned_order_blocks_market_expansion_without_changing_mandate(bool smallOrders, bool fundedOrders)
    {
        using var fixture = await Setup(multiMarkets: !smallOrders && !fundedOrders);
        Assert.True((await fixture.Provisioner.ProvisionAsync(fixture.Request, default)).Allowed);
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.True((await fixture.Services.GetRequiredService<LiveReconciliation>().InitializeBaselineAsync(
            new(fixture.Id, account.Mandate.Version, account.Snapshot.Version), default)).Complete);
        account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.True((await fixture.Services.GetRequiredService<LiveOwnerControls>().ArmAsync(
            new(fixture.Id, account.BrokerAccountReference!, account.Mandate.Version, account.Snapshot.Version, true, false))).Allowed);
        await fixture.Store.WithAccountLockAsync(fixture.Id, async (tx, ct) =>
        {
            var intent = new LiveIntent(fixture.Id, Guid.NewGuid(), Guid.NewGuid(), TradingMode.Live, "BTC-THB", OrderSide.Buy,
                .001m, 100000m, tx.Account.Mandate.Version, tx.Account.Snapshot.Version, "synthetic-owned-evidence") { BuyBudget = 100m };
            Assert.Equal(LiveOrderState.Reserved, tx.Reserve(intent, Now).State);
            await tx.CommitAsync(ct); return 0;
        });
        var result = await fixture.Activation.ActivateFullSpotAsync(default, smallOrders ? 100m : null, fundedOrders);
        Assert.False(result.Activated);
        Assert.Contains("UNRESOLVED_ORDER", result.Reasons);
        Assert.Equal(account.Mandate.Version, (await fixture.Store.ReadAccountAsync(fixture.Id)).Mandate.Version);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Owner_activation_requires_funds_and_trade_permission()
    {
        using var empty = await Setup();
        empty.Handler.Cash = 0;
        var noFunds = await empty.Activation.ActivateFullSpotAsync(default);
        Assert.False(noFunds.Activated);
        Assert.Contains("FUNDS_REQUIRED", noFunds.Reasons);
        Assert.False(await empty.Store.ExistsAsync(empty.Id));
        using var denied = await Setup();
        denied.Handler.TradePermission = false;
        var result = await denied.Activation.ActivateFullSpotAsync(default);
        Assert.False(result.Activated);
        Assert.Contains("TRADE_PERMISSION_REQUIRED", result.Reasons);
        Assert.False(await denied.Store.ExistsAsync(denied.Id));
    }

    [Fact]
    public async Task Owner_stop_is_preserved_by_activation()
    {
        using var fixture = await Setup();
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default)).Activated);
        await fixture.Services.GetRequiredService<LiveOwnerControls>().StopAsync();
        var result = await fixture.Activation.ActivateFullSpotAsync(default);
        Assert.False(result.Activated);
        Assert.Contains("OWNER_STOP_ACTIVE", result.Reasons);
        Assert.True((await fixture.Store.ReadAccountAsync(fixture.Id)).Gate.Stopped);
    }

    [Fact]
    public async Task Matching_signed_cash_wallet_provisions_once_without_arming_or_resetting_ledger()
    {
        using var fixture = await Setup();
        Assert.True((await fixture.Provisioner.ProvisionAsync(fixture.Request, default)).Allowed);
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(1000m, account.Snapshot.CashBalance);
        Assert.Equal(1, account.Mandate.Version);
        Assert.False(account.Gate.Armed);
        Assert.False(account.Snapshot.Reconciled);
        Assert.Null(account.FirstActivatedAt);
        Assert.Null(account.PerformanceBaseline);
        Assert.Empty(account.Orders);
        Assert.Equal(3, fixture.Handler.WalletReads);
        var result = await fixture.Provisioner.ProvisionAsync(fixture.Request, default);
        Assert.Contains("ACCOUNT_ALREADY_PROVISIONED", result.Violations);
        Assert.Equal(3, fixture.Handler.WalletReads);
        Assert.Equal(account.Snapshot.CashBalance, (await fixture.Store.ReadAccountAsync(fixture.Id)).Snapshot.CashBalance);
        Assert.Equal(account.Snapshot.Version, (await fixture.Store.ReadAccountAsync(fixture.Id)).Snapshot.Version);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_spot_provisioning_persists_exact_supported_opening_holdings_including_coin_only_funding(bool coinOnly)
    {
        using var fixture = await Setup();
        fixture.Handler.Cash = coinOnly ? 0m : 1000m;
        fixture.Handler.Btc = .00234567m;
        fixture.Handler.Bid = 99_999.12m;
        fixture.Handler.Ask = 100_000.12m;

        var result = await fixture.Provisioner.ProvisionFullSpotAsync(default);

        Assert.True(result.Allowed, string.Join(",", result.Violations));
        var account = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(fixture.Id);
        var equity = fixture.Handler.Cash + fixture.Handler.Btc * fixture.Handler.Bid;
        Assert.Equal(equity, account.Mandate.CapitalAllocation);
        Assert.Equal(equity, account.Mandate.MaximumOrderNotional);
        Assert.Equal(equity, account.Mandate.MaximumDailyTurnover);
        Assert.Equal(equity, account.Mandate.MaximumDailyLoss);
        Assert.Equal(1m, account.Mandate.MaximumPortfolioExposure);
        Assert.Equal(1m, account.Mandate.MaximumDrawdown);
        Assert.True(account.Mandate.AutomaticDailyRollover);
        Assert.Equal(fixture.Handler.Cash, account.Snapshot.CashBalance);
        Assert.Equal(0m, account.Snapshot.CashReserved);
        Assert.Equal(equity, account.Snapshot.Equity);
        var position = Assert.Single(account.Snapshot.Positions);
        Assert.Equal("BTC-THB", position.Symbol);
        Assert.Equal(fixture.Handler.Btc, position.Quantity);
        Assert.Equal(0m, position.ReservedQuantity);
        Assert.Equal(fixture.Handler.Bid, position.MarketPrice);
        Assert.NotNull(account.OpeningBalance);
        Assert.Equal(Now, account.OpeningBalance.ObservedAt);
        Assert.Equal("synthetic-account", account.OpeningBalance.BrokerAccountReference);
        Assert.Equal(fixture.Services.GetRequiredService<BitkubLiveBroker>().CredentialFingerprint, account.OpeningBalance.CredentialFingerprint);
        Assert.Equal(fixture.Handler.Cash, account.OpeningBalance.CashBalance);
        Assert.Equal(new LiveBrokerPosition("BTC-THB", fixture.Handler.Btc, 0m), Assert.Single(account.OpeningBalance.Positions));
        Assert.False(account.Gate.Armed);
        Assert.False(account.Snapshot.Reconciled);
        Assert.Null(account.PerformanceBaseline);
        Assert.Empty(account.Orders);
        Assert.Empty(account.Fills);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Explicit_allocation_must_include_the_full_marked_opening_holdings()
    {
        using var fixture = await Setup();
        fixture.Handler.Btc = .00234567m;
        var mismatch = await fixture.Provisioner.ProvisionAsync(fixture.Request, default);
        Assert.Contains("CAPITAL_ALLOCATION_MISMATCH", mismatch.Violations);
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));

        var equity = fixture.Handler.Cash + fixture.Handler.Btc * fixture.Handler.Bid;
        var result = await fixture.Provisioner.ProvisionAsync(fixture.Request with { CapitalAllocation = equity }, default);

        Assert.True(result.Allowed, string.Join(",", result.Violations));
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(equity, account.Snapshot.Equity);
        Assert.Equal(fixture.Handler.Btc, Assert.Single(account.Snapshot.Positions).Quantity);
        Assert.False(account.Gate.Armed);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Theory]
    [InlineData("cash-reserve")]
    [InlineData("coin-reserve")]
    [InlineData("foreign-open")]
    [InlineData("unsupported-coin")]
    public async Task Opening_holdings_require_no_reserves_foreign_opens_or_unsupported_coins(string defect)
    {
        using var fixture = await Setup();
        fixture.Handler.Btc = .00234567m;
        if (defect == "cash-reserve") fixture.Handler.CashReserved = 1m;
        if (defect == "coin-reserve") fixture.Handler.BtcReserved = .00000001m;
        if (defect == "foreign-open") fixture.Handler.ForeignOpen = true;
        if (defect == "unsupported-coin") fixture.Handler.Eth = 1m;

        if (defect == "foreign-open")
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Provisioner.ProvisionFullSpotAsync(default));
        else Assert.False((await fixture.Provisioner.ProvisionFullSpotAsync(default)).Allowed);

        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Changed_coin_quantity_between_signed_observations_cannot_create_an_account()
    {
        using var fixture = await Setup();
        fixture.Handler.Btc = .00234567m;
        fixture.Handler.ChangeCoinsAfterFirstRead = true;

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Provisioner.ProvisionFullSpotAsync(default));

        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Existing_manual_buy_before_opening_is_excluded_and_activation_cannot_reset_the_opening_balance()
    {
        using var fixture = await Setup();
        fixture.Handler.Btc = .00234567m;
        fixture.Handler.ManualHistoryAt = Now.AddSeconds(-1);

        var result = await fixture.Activation.ActivateFullSpotAsync(default);

        Assert.True(result.Activated, string.Join(",", result.Reasons));
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.True(account.Gate.Armed);
        Assert.True(account.Snapshot.Reconciled);
        Assert.Equal(fixture.Handler.Btc, Assert.Single(account.Snapshot.Positions).Quantity);
        Assert.Equal(account.Snapshot.Equity, account.PerformanceBaseline!.DayStartEquity);
        Assert.Empty(account.Orders);
        Assert.Empty(account.Fills);
        var reads = fixture.Handler.WalletReads;
        Assert.Contains("ACCOUNT_ALREADY_PROVISIONED", (await fixture.Provisioner.ProvisionFullSpotAsync(default)).Violations);
        Assert.Equal(reads, fixture.Handler.WalletReads);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(fixture.Id);
        Assert.Equal(JsonSerializer.Serialize(account.OpeningBalance), JsonSerializer.Serialize(restored.OpeningBalance));
        Assert.Equal(account.FirstActivatedAt, restored.FirstActivatedAt);
        Assert.Equal(account.Snapshot.Version, restored.Snapshot.Version);
        Assert.Equal(account.PerformanceBaseline, restored.PerformanceBaseline);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Manual_buy_at_or_after_opening_blocks_activation_without_importing_or_replaying_a_fill(int secondsAfterOpening)
    {
        using var fixture = await Setup();
        fixture.Handler.Btc = .00234567m;
        Assert.True((await fixture.Provisioner.ProvisionFullSpotAsync(default)).Allowed);
        var original = await fixture.Store.ReadAccountAsync(fixture.Id);
        fixture.Handler.ManualHistoryAt = original.OpeningBalance!.ObservedAt.AddSeconds(secondsAfterOpening);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));

        var result = await fixture.Activation.ActivateFullSpotAsync(default);

        Assert.False(result.Activated);
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.False(account.Gate.Armed);
        Assert.False(account.Snapshot.Reconciled);
        Assert.Null(account.PerformanceBaseline);
        Assert.Equal(original.Snapshot.CashBalance, account.Snapshot.CashBalance);
        Assert.Equal(fixture.Handler.Btc, Assert.Single(account.Snapshot.Positions).Quantity);
        Assert.Equal(JsonSerializer.Serialize(original.OpeningBalance), JsonSerializer.Serialize(account.OpeningBalance));
        Assert.Empty(account.Orders);
        Assert.Empty(account.Fills);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Manual_buy_since_opening_cannot_hide_before_first_activation()
    {
        using var fixture = await Setup();
        fixture.Handler.Btc = .00234567m;
        Assert.True((await fixture.Provisioner.ProvisionFullSpotAsync(default)).Allowed);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default)).Activated);
        var original = await fixture.Store.ReadAccountAsync(fixture.Id);
        fixture.Handler.ManualHistoryAt = original.OpeningBalance!.ObservedAt.AddSeconds(1);
        Assert.True(fixture.Handler.ManualHistoryAt < original.FirstActivatedAt);

        var result = await fixture.Services.GetRequiredService<LiveReconciliation>().ReconcileAsync(fixture.Id);

        Assert.False(result.Complete);
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.False(account.Gate.Armed);
        Assert.False(account.Snapshot.Reconciled);
        Assert.Equal(original.Snapshot.CashBalance, account.Snapshot.CashBalance);
        Assert.Equal(fixture.Handler.Btc, Assert.Single(account.Snapshot.Positions).Quantity);
        Assert.Equal(original.FirstActivatedAt, account.FirstActivatedAt);
        Assert.Equal(original.PerformanceBaseline, account.PerformanceBaseline);
        Assert.Equal(JsonSerializer.Serialize(original.OpeningBalance), JsonSerializer.Serialize(account.OpeningBalance));
        Assert.Empty(account.Orders);
        Assert.Empty(account.Fills);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("positions")]
    [InlineData("removed")]
    [InlineData("null")]
    public async Task Persisted_opening_balance_is_immutable_without_changing_ledger_amounts_or_history(string mutation)
    {
        using var fixture = await Setup();
        fixture.Handler.Btc = .00234567m;
        fixture.Handler.ManualHistoryAt = Now.AddSeconds(-1);
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default)).Activated);
        var original = await fixture.Store.ReadAccountAsync(fixture.Id);
        var changed = JsonSerializer.SerializeToNode(original)!.AsObject();
        if (mutation == "cash") changed["OpeningBalance"]!["CashBalance"] = original.OpeningBalance!.CashBalance + 1m;
        if (mutation == "positions") changed["OpeningBalance"]!["Positions"]![0]!["Quantity"] = fixture.Handler.Btc + .00000001m;
        if (mutation == "removed") changed.Remove("OpeningBalance");
        if (mutation == "null") changed["OpeningBalance"] = null;
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE live.accounts SET document=@document::jsonb,revision=revision+1 WHERE id=@id", connection);
        command.Parameters.AddWithValue("document", changed.ToJsonString());
        command.Parameters.AddWithValue("id", fixture.Id.Value);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal("23514", error.SqlState);
        var retained = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(fixture.Id);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(retained));
        Assert.Equal(original.Snapshot.CashBalance, retained.Snapshot.CashBalance);
        Assert.Equal(fixture.Handler.Btc, Assert.Single(retained.Snapshot.Positions).Quantity);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Changed_wallet_between_signed_observations_cannot_create_an_account()
    {
        using var fixture = await Setup();
        fixture.Handler.ChangeAfterFirstRead = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Provisioner.ProvisionAsync(fixture.Request, default));
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Owner_account_version_and_invalid_limits_are_rejected_before_provider_reads()
    {
        using var fixture = await Setup();
        foreach (var request in new[]
        {
            fixture.Request with { AccountId = new(Guid.NewGuid()) }, fixture.Request with { ExpectedVersion = 1 },
            fixture.Request with { MaximumDailyLoss = 0 }, fixture.Request with { FeeReserveRate = .001m },
            fixture.Request with { AllowedSymbols = ["ETH-THB"] }
        }) Assert.False((await fixture.Provisioner.ProvisionAsync(request, default)).Allowed);
        Assert.Empty(fixture.Handler.Paths);
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
    }

    [Fact]
    public async Task Allocating_a_different_cash_balance_cannot_create_or_arm_an_account()
    {
        using var fixture = await Setup();
        Assert.Contains("CAPITAL_ALLOCATION_MISMATCH", (await fixture.Provisioner.ProvisionAsync(
            fixture.Request with { CapitalAllocation = 999 }, default)).Violations);
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_activation_requires_baseline_and_matching_wallet_and_credentials(bool rotateCredentials)
    {
        using var fixture = await Setup();
        Assert.True((await fixture.Provisioner.ProvisionAsync(fixture.Request, default)).Allowed);
        var controls = fixture.Services.GetRequiredService<LiveOwnerControls>();
        Assert.False((await controls.PreflightAsync()).Ready);
        var account = await controls.ReadAsync();
        var reconciliation = fixture.Services.GetRequiredService<LiveReconciliation>();
        Assert.True((await reconciliation.InitializeBaselineAsync(new(fixture.Id, 1, account.Snapshot.Version))).Complete);
        account = await controls.ReadAsync();
        Assert.True((await controls.ArmAsync(new(fixture.Id, "synthetic-account", 1, account.Snapshot.Version, true, false))).Allowed);
        if (rotateCredentials) File.WriteAllText(fixture.Credentials, "BITKUB_API_KEY=rotated\nBITKUB_API_SECRET=test-secret\n");
        else fixture.Handler.Cash = 1001;
        Assert.False((await controls.PreflightAsync()).Ready);
        account = await controls.ReadAsync();
        Assert.False(account.Gate.Armed);
        Assert.False(account.Snapshot.Reconciled);
        Assert.Equal(1000, account.Snapshot.CashBalance);
        Assert.Empty(account.Orders);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Configured_missing_account_waits_without_reading_private_broker_or_sending()
    {
        using var fixture = await Setup();
        using var worker = new LiveReconciliationWorker(fixture.Services.GetRequiredService<LiveExecutionService>(),
            fixture.Services.GetRequiredService<LiveReconciliation>(), fixture.Id, fixture.Clock, fixture.Store);
        await worker.StartAsync(default);
        Assert.Equal("ACCOUNT_NOT_PROVISIONED", worker.Status);
        await worker.StopAsync(default);
        Assert.Empty(fixture.Handler.Paths);
    }

    [Fact]
    public async Task Account_activated_after_worker_start_is_not_treated_as_restart_recovery()
    {
        using var fixture = await Setup();
        using var worker = new LiveReconciliationWorker(fixture.Services.GetRequiredService<LiveExecutionService>(),
            fixture.Services.GetRequiredService<LiveReconciliation>(), fixture.Id, fixture.Clock, fixture.Store);
        await worker.StartAsync(default);
        await fixture.Clock.TimerReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default)).Activated);
        var previous = await fixture.Store.ReadAccountAsync(fixture.Id);
        fixture.Clock.Tick();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        LiveAccountState account;
        do
        {
            await Task.Delay(10, timeout.Token);
            account = await fixture.Store.ReadAccountAsync(fixture.Id, timeout.Token);
        } while (account.Snapshot.Version == previous.Snapshot.Version);
        Assert.True(account.Gate.Armed);
        Assert.True(account.Snapshot.Reconciled);
        Assert.DoesNotContain(account.Audit, a => a.Kind == "RecoverySuspended");
        Assert.Equal(0, fixture.Handler.OrderWrites);
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task Existing_armed_account_is_disarmed_before_executor_start_returns()
    {
        using var fixture = await Setup();
        Assert.True((await fixture.Activation.ActivateFullSpotAsync(default)).Activated);
        using var worker = new LiveReconciliationWorker(fixture.Services.GetRequiredService<LiveExecutionService>(),
            fixture.Services.GetRequiredService<LiveReconciliation>(), fixture.Id, fixture.Clock, fixture.Store);
        await worker.StartAsync(default);
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.False(account.Gate.Armed);
        Assert.Contains(account.Audit, a => a.Kind == "RecoverySuspended");
        Assert.Equal(1000m, account.Snapshot.CashBalance);
        Assert.Equal(0, fixture.Handler.OrderWrites);
        await worker.StopAsync(default);
    }

    private async Task<Fixture> Setup(bool multiMarkets = false)
    {
        var fixture = new Fixture(database.ConnectionString, multiMarkets);
        await fixture.Store.EnsureSchemaAsync();
        return fixture;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("quantara-onboarding-test-").FullName;
        public LiveAccountId Id { get; } = new(Guid.NewGuid());
        public string Credentials { get; }
        public TestClock Clock { get; } = new();
        public Handler Handler { get; }
        public ServiceProvider Services { get; }
        public LiveTradingStore Store => Services.GetRequiredService<LiveTradingStore>();
        public BitkubAccountProvisioner Provisioner => Services.GetRequiredService<BitkubAccountProvisioner>();
        public LiveOwnerActivation Activation => Services.GetRequiredService<LiveOwnerActivation>();
        public LiveMandateUpdate Request => new(Id, 0, 1000, 200, 500, 50, .1m, .2m, 1, .01m, .003m, ["BTC-THB"], Now.AddHours(1));
        public Fixture(string connectionString, bool multiMarkets = false)
        {
            Credentials = Path.Combine(directory, "credentials.env");
            File.WriteAllText(Credentials, "BITKUB_API_KEY=test-key\nBITKUB_API_SECRET=test-secret\n");
            Handler = new Handler(Clock);
            var broker = new BitkubLiveBroker("test-key", "test-secret", Handler, Clock);
            BitkubPairExecutionRules[] pairs = multiMarkets ? [new BitkubPairExecutionRules("BTC-THB", 1, .00000001m, .01m, 10, 0, 1),
                new BitkubPairExecutionRules("ETH-THB", 1, .00000001m, .01m, 10, 0, 1),
                new BitkubPairExecutionRules("SOL-THB", 1, .00000001m, .01m, 10, 0, 1)] :
                [new BitkubPairExecutionRules("BTC-THB", 1, .00000001m, .01m, 10, 0, 1)];
            var verification = new BitkubLiveVerification("synthetic-account", broker.CredentialFingerprint,
                "synthetic-account-confirmation", new string('a', 64), true, true,
                new("synthetic-rules-confirmation", new string('b', 64), .0025m,
                    BitkubBuyHistoryAmount.QuoteIncludingCashFee, pairs));
            var path = Path.Combine(directory, "verification.json");
            File.WriteAllText(path, JsonSerializer.Serialize(verification));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LiveExecution:Enabled"] = "true", ["LiveExecution:AccountId"] = Id.Value.ToString(),
                ["LiveExecution:VerificationFile"] = path, ["Bitkub:CredentialsFile"] = Credentials,
                ["ConnectionStrings:LiveTrading"] = connectionString
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            BitkubLiveRuntime.Configure(services, config);
            services.AddSingleton(broker);
            services.AddSingleton<LiveOwnerActivation>();
            Services = services.BuildServiceProvider();
        }
        public void Dispose() { Services.Dispose(); Directory.Delete(directory, true); }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = Now;
        private ManualTimer? timer;
        public TaskCompletionSource TimerReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            timer = new(callback, state);
            TimerReady.TrySetResult();
            return timer;
        }
        public void Tick() { now = now.AddSeconds(30); timer!.Fire(); }
        public void Advance(TimeSpan duration) { now = now.Add(duration); }
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            public void Fire() { if (!disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Handler(TestClock clock) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public int WalletReads { get; private set; }
        public int OrderWrites { get; private set; }
        public bool ChangeAfterFirstRead { get; set; }
        public bool ChangeCoinsAfterFirstRead { get; set; }
        public bool ForeignOpen { get; set; }
        public bool TradePermission { get; set; } = true;
        public decimal Cash { get; set; } = 1000;
        public decimal CashReserved { get; set; }
        public decimal Btc { get; set; }
        public decimal BtcReserved { get; set; }
        public decimal Eth { get; set; }
        public decimal Bid { get; set; } = 99_999m;
        public decimal Ask { get; set; } = 100_000m;
        public DateTimeOffset? ManualHistoryAt { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.EndsWith("/servertime")) return Reply(clock.GetUtcNow().ToUnixTimeMilliseconds().ToString());
            if (path.EndsWith("/wstoken")) return Reply(TradePermission ? "{\"error\":0,\"result\":\"synthetic-token-012345678901234567890123456789\"}" : "{\"error\":52,\"result\":null}");
            if (path.EndsWith("/balances"))
            {
                WalletReads++;
                var cash = ChangeAfterFirstRead && WalletReads > 1 ? Cash - 1 : Cash;
                var btc = ChangeCoinsAfterFirstRead && WalletReads > 1 ? Btc + .00000001m : Btc;
                return Reply(JsonSerializer.Serialize(new { code = "0", data = new[]
                {
                    Balance("THB", cash, CashReserved), Balance("BTC", btc, BtcReserved), Balance("ETH", Eth, 0m)
                }}));
            }
            if (path.EndsWith("/my-open-orders")) return Reply(JsonSerializer.Serialize(new { error = 0,
                result = ForeignOpen ? new[] { new { id = "manual-order", side = "buy", type = "limit", rate = "100000",
                    amount = "10", receive = "0", fee = "0", credit = "0", parent_id = "manual-order",
                    super_id = "manual-order", client_id = "manual-client", ts = clock.GetUtcNow().ToUnixTimeMilliseconds() } } : [] }));
            if (path.EndsWith("/my-order-history")) return Reply(JsonSerializer.Serialize(new { error = 0,
                result = ManualHistoryAt is { } at ? new[] { new { txn_id = "manual-buy-tx", order_id = "manual-buy-order",
                    parent_order_id = "manual-buy-order", super_order_id = "manual-buy-order", client_id = "manual-client",
                    side = "buy", type = "market", rate = "100000", amount = "235.154875", fee = "0.587875", credit = "0",
                    ts = at.ToUnixTimeMilliseconds() } } : [], pagination = new { has_next = false } }));
            if (path.EndsWith("/ticker"))
            {
                var requested = request.RequestUri!.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == "sym").Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault() ?? "BTC_THB";
                var symbol = requested.Replace('-', '_');
                return Reply(JsonSerializer.Serialize(new[] { new { symbol,
                highest_bid = Bid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lowest_ask = Ask.ToString(System.Globalization.CultureInfo.InvariantCulture) } }));
            }
            OrderWrites++;
            throw new InvalidOperationException("Unexpected provider operation in onboarding test.");
        }
        private static object Balance(string currency, decimal total, decimal reserved) => new
        {
            currency, available = (total - reserved).ToString(System.Globalization.CultureInfo.InvariantCulture),
            reserved = reserved.ToString(System.Globalization.CultureInfo.InvariantCulture), total = total.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        private static Task<HttpResponseMessage> Reply(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
