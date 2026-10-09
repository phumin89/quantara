using TradingCompany.Application;
using TradingCompany.Domain;
using System.Text.Json;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveSellSettlementTests
{
    private const decimal Quantity = .00001040m;
    private const decimal Price = 2800287.93m;
    private const decimal Gross = 29.12m;

    [Fact]
    public void Authenticated_sell_settlement_uses_credited_gross_and_keeps_gate_stopped()
    {
        var tx = Seed();
        var evidence = Evidence(Gross);

        var result = tx.Reconcile(evidence, evidence, LiveRiskTests.Now);

        Assert.True(result.Complete);
        Assert.Equal(29.12m, tx.Account.Snapshot.CashBalance);
        Assert.Empty(tx.Account.Snapshot.Positions);
        Assert.Equal(LiveOrderState.Filled, Assert.Single(tx.Account.Orders).State);
        Assert.Equal(0m, Assert.Single(tx.Account.Orders).ReservedQuantity);
        Assert.Equal(Quantity, Assert.Single(tx.Account.Fills).Quantity);
        Assert.Equal(Price, Assert.Single(tx.Account.Fills).Price);
        Assert.False(tx.Account.Gate.Armed);
        Assert.True(tx.Account.Gate.Stopped);
    }

    [Fact]
    public void Repeating_same_settlement_does_not_credit_cash_twice()
    {
        var tx = Seed();
        var evidence = Evidence(Gross);

        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        var version = tx.Account.Snapshot.Version;
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);

        Assert.Equal(Gross, tx.Account.Snapshot.CashBalance);
        Assert.Single(tx.Account.Fills);
        Assert.True(tx.Account.Snapshot.Version > version);
    }

    [Fact]
    public void Settlement_adjustment_survives_account_reload_without_cash_or_turnover_drift()
    {
        var tx = Seed();
        var evidence = Evidence(Gross);
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        var cash = tx.Account.Snapshot.CashBalance;
        var turnover = tx.Account.Snapshot.DailyTurnover;
        var adjustment = Assert.Single(tx.Account.Orders).SellQuoteAdjustment;
        var restored = JsonSerializer.Deserialize<LiveAccountState>(JsonSerializer.Serialize(tx.Account))!;
        var reloaded = new LiveAccountTransaction(restored, (_, _) => Task.CompletedTask);

        Assert.True(reloaded.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);

        Assert.Equal(cash, reloaded.Account.Snapshot.CashBalance);
        Assert.Equal(turnover, reloaded.Account.Snapshot.DailyTurnover);
        Assert.Equal(adjustment, Assert.Single(reloaded.Account.Orders).SellQuoteAdjustment);
        Assert.Single(reloaded.Account.Fills);
    }

    [Fact]
    public void Partial_then_filled_sell_applies_only_new_cumulative_settlement_delta()
    {
        var tx = Seed(Quantity * 2m);
        var secondPrice = 2800765.43m;
        var first = Evidence(Gross, Quantity, LiveOrderState.PartiallyFilled,
            [new BrokerFill(LiveRiskTests.Account, "sell-fill-1", "owned-order", OrderSide.Sell,
                "BTC-THB", Quantity, Price, 0m, 1)],
            Price, [new LiveBrokerPosition("BTC-THB", Quantity, Quantity)]);
        Assert.True(tx.Reconcile(first, first, LiveRiskTests.Now).Complete);
        var firstFill = Assert.Single(tx.Account.Fills);
        var firstAdjustment = Assert.Single(tx.Account.Orders).SellQuoteAdjustment;
        Assert.True(tx.Reconcile(first, first, LiveRiskTests.Now).Complete);
        Assert.Equal(Gross, tx.Account.Snapshot.CashBalance);
        Assert.Equal(firstAdjustment, Assert.Single(tx.Account.Orders).SellQuoteAdjustment);

        const decimal finalGross = 58.25m;
        var final = Evidence(finalGross, Quantity * 2m, LiveOrderState.Filled,
            [firstFill, new BrokerFill(LiveRiskTests.Account, "sell-fill-2", "owned-order", OrderSide.Sell,
                "BTC-THB", Quantity, secondPrice, 0m, 1)]);
        Assert.True(tx.Reconcile(final, final, LiveRiskTests.Now).Complete);

        Assert.Equal(finalGross, tx.Account.Snapshot.CashBalance);
        Assert.Equal(finalGross, tx.Account.Snapshot.DailyTurnover);
        Assert.Collection(tx.Account.Fills, _ => { }, _ => { });
        Assert.Equal(firstFill, tx.Account.Fills.Single(f => f.TransactionId == "sell-fill-1"));
        Assert.Equal(finalGross - Quantity * (Price + secondPrice), Assert.Single(tx.Account.Orders).SellQuoteAdjustment);
        Assert.NotEqual(firstAdjustment, Assert.Single(tx.Account.Orders).SellQuoteAdjustment);
    }

    [Fact]
    public void Wallet_mismatch_rolls_back_settlement_and_fills()
    {
        var tx = Seed();
        var before = tx.Account;
        var evidence = Evidence(Gross) with { CashBalance = Gross + .01m };

        Assert.False(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);

        Assert.Equal(before.Snapshot.CashBalance, tx.Account.Snapshot.CashBalance);
        Assert.Empty(tx.Account.Fills);
        Assert.Equal(before.Orders, tx.Account.Orders);
    }

    [Fact]
    public void Sub_cent_sell_match_can_settle_zero_cash_without_inventing_proceeds()
    {
        const decimal quantity = .00000001m;
        const decimal price = 100000m;
        var tx = Seed(quantity, price);
        var evidence = Evidence(0m, quantity, LiveOrderState.Filled, price: price);

        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(0m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(0m, tx.Account.Snapshot.DailyTurnover);
        Assert.Equal(-.001m, Assert.Single(tx.Account.Orders).SellQuoteAdjustment);
    }

    [Fact]
    public void Exact_one_cent_per_fill_discrepancy_fails_closed()
    {
        const decimal quantity = .00001m;
        const decimal price = 3000000m;
        var tx = Seed(quantity, price);
        var evidence = Evidence(29.99m, quantity, LiveOrderState.Filled,
            [new BrokerFill(LiveRiskTests.Account, "sell-fill", "owned-order", OrderSide.Sell,
                "BTC-THB", quantity, price, 0m, 1)], price);

        Assert.False(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Empty(tx.Account.Fills);
        Assert.Equal(0m, tx.Account.Snapshot.CashBalance);
    }

    [Fact]
    public void Buy_order_cannot_carry_sell_settlement_amount()
    {
        var tx = LiveReconciliationTests.Seed();
        var evidence = LiveReconciliationTests.Evidence();
        evidence = evidence with
        {
            Orders = [new LiveBrokerOrder(LiveRiskTests.Intent.Id.ToString("N"), "owned-order", "BTC-THB",
                OrderSide.Buy, .001m, 100000m, LiveOrderState.Filled, .001m, 0m, 0m)
            { SettledSellQuoteAmount = 10m }]
        };

        Assert.False(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Empty(tx.Account.Fills);
    }

    [Fact]
    public void Settlement_mismatch_at_one_cent_per_fill_fails_closed_without_account_mutation()
    {
        var tx = Seed();
        var before = tx.Account;
        var evidence = Evidence(Gross - .01m);

        Assert.False(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);

        Assert.Equal(before.Snapshot.CashBalance, tx.Account.Snapshot.CashBalance);
        Assert.Equal(before.Snapshot.Positions, tx.Account.Snapshot.Positions);
        Assert.Empty(tx.Account.Fills);
        Assert.Equal(before.Orders, tx.Account.Orders);
        Assert.False(tx.Account.Gate.Armed);
        Assert.True(tx.Account.Gate.Stopped);
    }

    private static LiveAccountTransaction Seed(decimal quantity = Quantity, decimal price = Price)
    {
        var snapshot = LiveRiskTests.Snapshot with
        {
            CashBalance = 0m,
            CashReserved = 0m,
            Positions = [new("BTC-THB", quantity, 0m, price)],
            Equity = quantity * price,
            Quotes = [new("BTC-THB", price, price, LiveRiskTests.Now)]
        };
        var account = new LiveAccountState(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            snapshot, [], [], [])
        {
            PerformanceBaseline = new(DateOnly.FromDateTime(LiveRiskTests.Now.DateTime), snapshot.Equity, snapshot.Equity)
        };
        var tx = new LiveAccountTransaction(account, (_, _) => Task.CompletedTask);
        var intent = LiveRiskTests.Intent with { Side = OrderSide.Sell, Quantity = quantity, LimitPrice = price };
        tx.Reserve(intent, LiveRiskTests.Now);
        tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "owned-order", LiveRiskTests.Now);
        tx.SetStopped(true);
        return tx;
    }

    private static LiveBrokerEvidence Evidence(decimal settledGross, decimal quantity = Quantity,
        LiveOrderState state = LiveOrderState.Filled, IReadOnlyList<BrokerFill>? fills = null, decimal price = Price,
        IReadOnlyList<LiveBrokerPosition>? positions = null)
        => new(LiveRiskTests.Account, "key-fingerprint", LiveRiskTests.Now, true,
            settledGross, 0m, positions ?? [],
            [new LiveBrokerOrder(LiveRiskTests.Intent.Id.ToString("N"), "owned-order", "BTC-THB", OrderSide.Sell,
                quantity * (state == LiveOrderState.PartiallyFilled ? 2m : 1m), price, state, quantity, 0m,
                state == LiveOrderState.PartiallyFilled ? quantity : 0m)
            { SettledSellQuoteAmount = settledGross }],
            fills ?? [new(LiveRiskTests.Account, "sell-fill", "owned-order", OrderSide.Sell, "BTC-THB", quantity, price, 0m, 1)],
            positions is { Count: > 0 } ? [new("BTC-THB", price, price, LiveRiskTests.Now)] : []);
}
