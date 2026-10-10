using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveTradePlanPersistenceTests(PostgresFixture database)
{
    private static readonly LiveOrderSizing Rules = new("BTC-THB", .01m, .00000001m, 1, 10);

    [Fact]
    public async Task Explicit_full_portfolio_policy_survives_store_restart_without_activation_or_balance_reset()
    {
        var f = await Setup();
        var before = await f.Store.ReadAccountAsync(f.Plan.AccountId);
        await f.Store.WithAccountLockAsync(f.Plan.AccountId, async (tx, ct) =>
        {
            var capital = tx.Account.Snapshot.Equity;
            var result = tx.SaveMandate(tx.Account.Mandate with { Version = tx.Account.Mandate.Version + 1,
                UseFullPortfolio = true, CapitalAllocation = capital, MaximumOrderNotional = capital,
                MaximumDailyTurnover = capital, MaximumDailyLoss = capital, MaximumPortfolioExposure = 1,
                MaximumDrawdown = 1 }, f.Clock.Now);
            Assert.True(result.Allowed);
            await tx.CommitAsync(ct);
            return true;
        });
        var after = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(f.Plan.AccountId);
        Assert.True(after.Mandate.UseFullPortfolio);
        Assert.False(after.Gate.Armed);
        Assert.False(after.Snapshot.Reconciled);
        Assert.Equal(before.Snapshot.CashBalance, after.Snapshot.CashBalance);
        Assert.Equal(before.Snapshot.DailyLoss, after.Snapshot.DailyLoss);
        Assert.Equal(before.PerformanceBaseline, after.PerformanceBaseline);
        Assert.Equal(0, f.Broker.PlacementCount);
    }

    [Fact]
    public async Task Immutable_plan_survives_restart_and_conflicting_replay_preserves_authority()
    {
        var f = await Setup();
        var before = await f.Store.ReadAccountAsync(f.Plan.AccountId);
        Assert.True((await f.Service.RegisterAsync(f.Plan)).Accepted);
        var restored = new LiveTradingStore(database.ConnectionString);
        var service = Service(restored, f.Broker, f.Clock);
        Assert.True((await service.RegisterAsync(f.Plan)).Accepted);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RegisterAsync(f.Plan with { Plan = f.Plan.Plan with { Quantity = .0015m } }));
        var after = await restored.ReadAccountAsync(f.Plan.AccountId);
        Assert.Equal(f.Plan, Assert.Single(LiveTradePlans.Read(after)));
        Assert.Single(after.Audit, a => a.Kind == LiveTradePlans.Created);
        Assert.Equal(JsonSerializer.Serialize(before.Mandate), JsonSerializer.Serialize(after.Mandate));
        Assert.Equal(JsonSerializer.Serialize(before.Snapshot), JsonSerializer.Serialize(after.Snapshot));
        Assert.Equal(before.Gate, after.Gate);
        Assert.Equal(0, f.Broker.PlacementCount);
    }

    [Theory]
    [InlineData("stopped")] [InlineData("disarmed")] [InlineData("stale")] [InlineData("mandate")]
    [InlineData("cash")] [InlineData("expiry")] [InlineData("fees")] [InlineData("round-trip")]
    public async Task Rejected_registration_never_writes_a_plan_or_order(string defect)
    {
        var f = await Setup(a => defect switch
        {
            "stopped" => a with { Gate = a.Gate with { Stopped = true } },
            "disarmed" => a with { Gate = a.Gate with { Armed = false } },
            "stale" => a with { Snapshot = a.Snapshot with { ObservedAt = a.Snapshot.ObservedAt.AddSeconds(-31) } },
            "cash" => a with { Snapshot = a.Snapshot with { CashBalance = 100, Equity = 100 } },
            "round-trip" => a with { Mandate = a.Mandate with { MaximumDailyTurnover = 250 } }, _ => a
        });
        var p = defect switch
        {
            "mandate" => f.Plan with { MandateVersion = 2 },
            "expiry" => f.Plan with { Plan = f.Plan.Plan with { ExpiresAt = f.Clock.Now.AddMinutes(1) } },
            "fees" => f.Plan with { Plan = f.Plan.Plan with { TakeProfitPrice = 100001 } }, _ => f.Plan
        };
        var result = await f.Service.RegisterAsync(p);
        Assert.False(result.Accepted); Assert.NotEmpty(result.Violations);
        Assert.Empty(LiveTradePlans.Read(await f.Store.ReadAccountAsync(p.AccountId)));
        Assert.Equal(0, f.Broker.PlacementCount);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_monitors_and_restart_send_entry_once_even_when_reply_is_lost(bool unknown)
    {
        var f = await Setup();
        Assert.True((await f.Service.RegisterAsync(f.Plan)).Accepted);
        if (unknown) f.Broker.OnPlace = _ => throw new IOException("simulated lost reply");
        var second = Service(new LiveTradingStore(database.ConnectionString), f.Broker, f.Clock);
        await Task.WhenAll(f.Service.TickAsync(f.Plan.AccountId), second.TickAsync(f.Plan.AccountId));
        await second.TickAsync(f.Plan.AccountId);
        var after = await f.Store.ReadAccountAsync(f.Plan.AccountId);
        Assert.Equal(1, f.Broker.PlacementCount);
        Assert.Equal(unknown ? LiveOrderState.Unknown : LiveOrderState.Acknowledged, Assert.Single(after.Orders).State);
        Assert.Equal(LiveTradePlans.IntentId(f.Plan.Id, OrderSide.Buy), after.Orders[0].Intent.Id);
        if (unknown) Assert.False(after.Gate.Armed);
    }

    [Theory]
    [InlineData("above")] [InlineData("invalidated-price")] [InlineData("expired")]
    [InlineData("stale-quote")] [InlineData("stopped")] [InlineData("mandate")]
    public async Task Waiting_entry_obeys_prices_expiry_and_owner_authority(string condition)
    {
        var f = await Setup();
        Assert.True((await f.Service.RegisterAsync(f.Plan)).Accepted);
        if (condition == "expired") f.Clock.Now = f.Plan.Plan.ExpiresAt;
        else if (condition is "above" or "invalidated-price")
            await Refresh(f.Store, f.Plan.AccountId, f.Clock, condition == "above" ? 100001 : 94000);
        else if (condition == "stale-quote") f.Clock.Now = f.Clock.Now.AddSeconds(31);
        else await f.Store.WithAccountLockAsync(f.Plan.AccountId, async (tx, ct) =>
        {
            if (condition == "stopped") tx.SetStopped(true);
            else Assert.True(tx.SaveMandate(tx.Account.Mandate with { Version = 2 }, f.Clock.Now).Allowed);
            await tx.CommitAsync(ct); return 0;
        });
        await f.Service.TickAsync(f.Plan.AccountId);
        Assert.Equal(0, f.Broker.PlacementCount);
        Assert.Empty((await f.Store.ReadAccountAsync(f.Plan.AccountId)).Orders);
        if (condition == "invalidated-price")
        {
            await Refresh(f.Store, f.Plan.AccountId, f.Clock, 100000);
            await Service(new LiveTradingStore(database.ConnectionString), f.Broker, f.Clock).TickAsync(f.Plan.AccountId);
            Assert.Equal(0, f.Broker.PlacementCount);
            Assert.Equal("INVALIDATED", Assert.Single(LiveTradePlans.Views(await f.Store.ReadAccountAsync(f.Plan.AccountId), f.Clock.Now)).Status);
        }
    }

    [Theory]
    [InlineData("target", false)] [InlineData("stop", false)] [InlineData("expiry", false)] [InlineData("target", true)]
    public async Task Exit_uses_actual_fill_and_preserves_imported_coins(string trigger, bool partial)
    {
        var f = await Setup(a => a with { Snapshot = a.Snapshot with
        { Positions = [new("BTC-THB", .0000104m, 0, 100000)], Equity = 1001.04m } });
        Assert.True((await f.Service.RegisterAsync(f.Plan)).Accepted);
        f.Broker.OnPlace = r => Task.FromResult(new BrokerSubmission(BrokerSubmissionState.Accepted, r.Intent.Id.ToString("N"), "TEST_ACCEPTED"));
        await f.Service.TickAsync(f.Plan.AccountId);
        var entry = Assert.Single((await f.Store.ReadAccountAsync(f.Plan.AccountId)).Orders);
        var bought = partial ? .0007m : .001396m;
        await Fill(f.Store, entry, bought, 100000, partial ? LiveOrderState.Cancelled : LiveOrderState.Filled, f.Clock);
        await Refresh(f.Store, f.Plan.AccountId, f.Clock, 100000);
        await f.Store.WithAccountLockAsync(f.Plan.AccountId, (tx, ct) =>
        {
            var unplannedSell = entry.Intent with { Id = Guid.NewGuid(), TradePlanId = null, BuyBudget = null,
                Side = OrderSide.Sell, Quantity = bought, SnapshotVersion = tx.Account.Snapshot.Version };
            var rejected = tx.Reserve(unplannedSell, f.Clock.Now);
            Assert.Equal(LiveOrderState.Rejected, rejected.State);
            Assert.Contains("PLAN_QUANTITY_RESERVED", rejected.Violations);
            return Task.FromResult(0); // isolated assertion, no extra order persisted
        });
        await f.Service.TickAsync(f.Plan.AccountId);
        Assert.Equal(1, f.Broker.PlacementCount);
        if (trigger == "expiry") f.Clock.Now = f.Plan.Plan.ExpiresAt;
        await Refresh(f.Store, f.Plan.AccountId, f.Clock, trigger == "target" ? 110000 : trigger == "stop" ? 94000 : 100000);
        var restarted = Service(new LiveTradingStore(database.ConnectionString), f.Broker, f.Clock);
        await restarted.TickAsync(f.Plan.AccountId);
        var exit = (await f.Store.ReadAccountAsync(f.Plan.AccountId)).Orders.Single(o => o.Intent.Side == OrderSide.Sell);
        Assert.Equal(bought, exit.Intent.Quantity); Assert.NotEqual(f.Plan.Plan.Quantity, exit.Intent.Quantity);
        Assert.Equal(2, f.Broker.PlacementCount);
        await restarted.TickAsync(f.Plan.AccountId); Assert.Equal(2, f.Broker.PlacementCount);
        await Fill(f.Store, exit, bought, exit.Intent.LimitPrice, LiveOrderState.Filled, f.Clock);
        await Refresh(f.Store, f.Plan.AccountId, f.Clock, exit.Intent.LimitPrice);
        var after = await f.Store.ReadAccountAsync(f.Plan.AccountId);
        Assert.Equal(.0000104m, Assert.Single(after.Snapshot.Positions).Quantity);
        var view = Assert.Single(LiveTradePlans.Views(after, f.Clock.Now));
        Assert.Equal("CLOSED", view.Status); Assert.Equal(bought, view.AcquiredQuantity); Assert.Equal(bought, view.SoldQuantity);
    }

    [Fact]
    public async Task Expired_open_entry_cancels_once_and_retains_reservation_until_reconciliation()
    {
        var f = await Setup();
        Assert.True((await f.Service.RegisterAsync(f.Plan)).Accepted);
        await f.Service.TickAsync(f.Plan.AccountId);
        f.Clock.Now = f.Plan.Plan.ExpiresAt;
        await f.Service.TickAsync(f.Plan.AccountId);
        await Service(new LiveTradingStore(database.ConnectionString), f.Broker, f.Clock).TickAsync(f.Plan.AccountId);
        var after = await f.Store.ReadAccountAsync(f.Plan.AccountId);
        Assert.Equal(1, f.Broker.CancellationCount); Assert.False(after.Gate.Armed); Assert.False(after.Snapshot.Reconciled);
        Assert.True(after.Snapshot.CashReserved > 0);
        Assert.Equal("REVIEW_REQUIRED", Assert.Single(LiveTradePlans.Views(after, f.Clock.Now)).Status);
    }

    [Fact]
    public async Task Tiny_partial_fill_is_reported_for_review_instead_of_claiming_an_executable_exit()
    {
        var f = await Setup(); Assert.True((await f.Service.RegisterAsync(f.Plan)).Accepted);
        await f.Service.TickAsync(f.Plan.AccountId);
        var entry = Assert.Single((await f.Store.ReadAccountAsync(f.Plan.AccountId)).Orders);
        await Fill(f.Store, entry, .00001m, 100000, LiveOrderState.Cancelled, f.Clock);
        await Refresh(f.Store, f.Plan.AccountId, f.Clock, 110000);
        await f.Service.TickAsync(f.Plan.AccountId);
        Assert.Equal(1, f.Broker.PlacementCount);
        Assert.Equal("REVIEW_REQUIRED", Assert.Single(LiveTradePlans.Views(await f.Store.ReadAccountAsync(f.Plan.AccountId), f.Clock.Now, [Rules])).Status);
    }

    private async Task<(LiveTradingStore Store, LiveTradePlanService Service, LiveTradePlanRequest Plan,
        LiveExecutionFlowTests.FakeBroker Broker, LiveExecutionFlowTests.TestClock Clock)> Setup(Func<LiveAccountState, LiveAccountState>? change = null)
    {
        var (store, a, _) = await new LiveTradingStoreTests(database).Setup();
        if (change is not null)
        {
            var id = new LiveAccountId(Guid.NewGuid()); var changed = change(a);
            a = changed with { AccountId = id, Mandate = changed.Mandate with { AccountId = id }, Snapshot = changed.Snapshot with { AccountId = id } };
            await store.CreateAsync(a);
        }
        var clock = new LiveExecutionFlowTests.TestClock(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.FromHours(7)));
        var broker = new LiveExecutionFlowTests.FakeBroker();
        var plan = new LiveTradePlanRequest(a.AccountId, Guid.NewGuid(), Guid.NewGuid(), a.Mandate.Version, a.Snapshot.Version,
            "BTC-THB", new string('a', 64), new(100000, .0014m, 110000, 95000, clock.Now.AddHours(4)));
        return (store, Service(store, broker, clock), plan, broker, clock);
    }
    private static LiveTradePlanService Service(ILiveTradingStore store, ILiveBroker broker, TimeProvider clock) => new(store, new(store, broker, clock), clock, [Rules]);
    private static Task Fill(LiveTradingStore store, LiveOrderRecord o, decimal quantity, decimal price, LiveOrderState terminal,
        LiveExecutionFlowTests.TestClock clock) => store.WithAccountLockAsync(o.Intent.AccountId, async (tx, ct) =>
    {
        tx.ApplyFill(new(o.Intent.AccountId, o.Intent.Id.ToString("N"), o.BrokerId!, o.Intent.Side, o.Intent.Symbol, quantity, price, .25m, 1), clock.Now);
        tx.RecordTransition(o.Intent.Id, terminal, o.BrokerId, clock.Now); await tx.CommitAsync(ct); return 0;
    });
    private static Task Refresh(LiveTradingStore store, LiveAccountId id, LiveExecutionFlowTests.TestClock clock, decimal bid) => store.WithAccountLockAsync(id, async (tx, ct) =>
    {
        var a = tx.Account;
        var e = new LiveBrokerEvidence(id, a.Mandate.CredentialFingerprint, clock.Now, true, a.Snapshot.CashBalance, a.Snapshot.CashReserved,
            a.Snapshot.Positions.Select(p => new LiveBrokerPosition(p.Symbol, p.Quantity, p.ReservedQuantity)).ToArray(),
            a.Orders.Where(o => o.State != LiveOrderState.Rejected).Select(o => new LiveBrokerOrder(o.Intent.Id.ToString("N"), o.BrokerId!, o.Intent.Symbol,
                o.Intent.Side, o.Intent.Quantity, o.Intent.LimitPrice, o.State, o.FilledQuantity, o.ReservedCash, o.ReservedQuantity)
                { BuyBudget = o.Intent.BuyBudget }).ToArray(), a.Fills, [new("BTC-THB", bid, bid, clock.Now)]);
        var result = tx.Reconcile(e, e, clock.Now); Assert.True(result.Complete, string.Join(",", result.Violations));
        await tx.CommitAsync(ct); return 0;
    });
}
