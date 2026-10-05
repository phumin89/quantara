using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveBuyBudgetTests
{
    [Fact]
    public void Price_improvement_can_buy_more_coins_without_exceeding_cash_budget()
    {
        var tx = Seed();
        tx.ApplyFill(Fill(), LiveRiskTests.Now); // .00499 * 80000 + .8 = 400
        Assert.Equal(600m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(.00499m, Assert.Single(tx.Account.Snapshot.Positions).Quantity);
        Assert.Equal(1.2m, tx.Account.Snapshot.CashReserved);
        Assert.True(tx.Account.Snapshot.PendingBuyNotional >= 0);
        Assert.Contains("BTC-THB", tx.Account.Snapshot.PendingBuySymbols);
        tx.RecordTransition(LiveRiskTests.Intent.Id, LiveOrderState.Filled, null, LiveRiskTests.Now);
        Assert.Equal(0m, tx.Account.Snapshot.CashReserved);
    }

    [Fact]
    public void Partial_budget_order_retains_exposure_even_after_reference_quantity_is_exceeded()
    {
        var tx = Seed();
        tx.ApplyFill(Fill() with { Quantity = .0045m, CashFee = .9m }, LiveRiskTests.Now);
        Assert.Equal(639.1m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(40.3m, tx.Account.Snapshot.CashReserved);
        Assert.Equal(40.3m, tx.Account.Snapshot.PendingBuyNotional);
        Assert.Contains("BTC-THB", tx.Account.Snapshot.PendingBuySymbols);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cash_fee_cannot_overrun_budget_even_when_account_has_more_cash(bool correction)
    {
        var tx = Seed();
        if (correction) tx.ApplyFill(Fill(), LiveRiskTests.Now);
        var before = tx.Account;
        Assert.Throws<InvalidDataException>(() => tx.ApplyFill(Fill() with
            { Quantity = correction ? .00499m : .004m, CashFee = correction ? .81m : 81m, Revision = correction ? 2 : 1 }, LiveRiskTests.Now));
        Assert.Equal(before, tx.Account);
    }

    [Theory]
    [InlineData(0, OrderSide.Buy)]
    [InlineData(-1, OrderSide.Buy)]
    [InlineData(401, OrderSide.Buy)]
    [InlineData(400, OrderSide.Sell)]
    public void Invalid_budget_never_passes_risk(decimal budget, OrderSide side)
    {
        var result = LiveRiskPolicy.Evaluate(LiveRiskTests.Intent with { BuyBudget = budget, Side = side },
            LiveRiskTests.Snapshot, LiveRiskTests.Mandate, LiveRiskTests.Gate, LiveRiskTests.Now);
        Assert.False(result.Allowed);
        Assert.Contains("INVALID_INTENT", result.Violations);
    }

    [Fact]
    public void Legacy_intent_still_enforces_exact_quantity_and_loads_without_budget()
    {
        var oldJson = JsonSerializer.SerializeToNode(LiveRiskTests.Intent)!.AsObject();
        oldJson.Remove("BuyBudget");
        var restored = oldJson.Deserialize<LiveIntent>()!;
        Assert.Null(restored.BuyBudget);
        Assert.Equal(LiveRiskTests.Intent, restored);
        var tx = Seed(restored);
        Assert.Throws<InvalidDataException>(() => tx.ApplyFill(Fill(), LiveRiskTests.Now));
        Assert.Equal(1000m, tx.Account.Snapshot.CashBalance);
    }

    [Fact]
    public void Budget_identity_survives_json_and_cannot_reuse_old_intent_id()
    {
        var intent = LiveRiskTests.Intent with { BuyBudget = 400m };
        Assert.Equal(intent, JsonSerializer.Deserialize<LiveIntent>(JsonSerializer.Serialize(intent)));
        var tx = Seed();
        Assert.Throws<InvalidDataException>(() => tx.Reserve(intent with { BuyBudget = null }, LiveRiskTests.Now));
    }

    [Theory]
    [InlineData(LiveOrderState.Filled, .00499, .8, 0)]
    [InlineData(LiveOrderState.Filled, .003, .6, 0)]
    [InlineData(LiveOrderState.PartiallyFilled, .0045, .9, 39.1)]
    public void Budget_status_and_wallet_reconcile_without_exact_coin_target(LiveOrderState state, decimal coins, decimal fee, decimal reserved)
    {
        var tx = Seed();
        var fill = Fill() with { Quantity = coins, CashFee = fee };
        var evidence = Evidence(fill, state, reserved);
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(1000m - coins * 80000m - fee, tx.Account.Snapshot.CashBalance);
        Assert.Equal(state, Assert.Single(tx.Account.Orders).State);
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Single(tx.Account.Fills);
    }

    [Fact]
    public void Unbound_or_over_budget_evidence_cannot_commit_any_money()
    {
        foreach (var defect in new[] { "missing-budget", "overspend", "zero-filled" })
        {
            var tx = Seed();
            var evidence = Evidence(Fill(), LiveOrderState.Filled, 0);
            if (defect == "missing-budget") evidence = evidence with { Orders = [evidence.Orders[0] with { BuyBudget = null }] };
            if (defect == "overspend") evidence = evidence with { CashBalance = 599.99m, Fills = [Fill() with { CashFee = .81m }] };
            if (defect == "zero-filled") evidence = evidence with { Orders = [evidence.Orders[0] with { FilledQuantity = 0 }] };
            Assert.False(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
            Assert.Equal(1000m, tx.Account.Snapshot.CashBalance);
            Assert.Equal(401.2m, tx.Account.Snapshot.CashReserved);
            Assert.Empty(tx.Account.Fills);
        }
    }

    private static LiveAccountTransaction Seed(LiveIntent? intent = null)
    {
        var tx = new LiveAccountTransaction(new(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot, [], [], []) { PerformanceBaseline = new(new(2026, 9, 27), 1000m, 1000m) },
            (_, _) => Task.CompletedTask);
        intent ??= LiveRiskTests.Intent with { BuyBudget = 400m };
        Assert.Equal(LiveOrderState.Reserved, tx.Reserve(intent, LiveRiskTests.Now).State);
        tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "budget-order", LiveRiskTests.Now);
        tx.SetStopped(true);
        return tx;
    }

    private static BrokerFill Fill() => new(LiveRiskTests.Account, "budget-fill", "budget-order", OrderSide.Buy,
        "BTC-THB", .00499m, 80000m, .8m, 1);

    private static LiveBrokerEvidence Evidence(BrokerFill fill, LiveOrderState state, decimal reserved) => new(
        LiveRiskTests.Account, "key-fingerprint", LiveRiskTests.Now, true, 1000m - fill.Quantity * fill.Price - fill.CashFee,
        reserved, [new("BTC-THB", fill.Quantity, 0)],
        [new(LiveRiskTests.Intent.Id.ToString("N"), "budget-order", "BTC-THB", OrderSide.Buy, .004m, 100000m,
            state, fill.Quantity, reserved, 0) { BuyBudget = 400m }], [fill], LiveRiskTests.Snapshot.Quotes);
}
