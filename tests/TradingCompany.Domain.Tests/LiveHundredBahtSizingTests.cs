using TradingCompany.Domain;
using TradingCompany.Application;
using System.Text.Json;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveHundredBahtSizingTests
{
    private static LiveMandate HundredBahtMandate => LiveRiskTests.Mandate with
    {
        MaximumOrderNotional = 100m, AllowedSymbols = ["BTC-THB", "ETH-THB", "SOL-THB"]
    };
    public static IEnumerable<object[]> Symbols() =>
    [
        ["BTC-THB", 100000m],
        ["ETH-THB", 3000m],
        ["SOL-THB", 150m]
    ];

    [Theory]
    [MemberData(nameof(Symbols))]
    public void Buy_at_hundred_baht_gross_including_fee_is_allowed(string symbol, decimal price)
    {
        var budget = decimal.Round(99.99m / (1m + HundredBahtMandate.FeeReserveRate), 8);
        var quantity = budget / price;
        var intent = LiveRiskTests.Intent with
        {
            Symbol = symbol,
            LimitPrice = price,
            Quantity = quantity,
            BuyBudget = quantity * price
        };

        var result = LiveRiskPolicy.Evaluate(intent, LiveRiskTests.Snapshot with
        {
            Quotes = [new(symbol, price - 1m, price, LiveRiskTests.Now)]
        }, HundredBahtMandate, LiveRiskTests.Gate, LiveRiskTests.Now);

        Assert.True(result.Allowed, string.Join(",", result.Violations));
        Assert.True(intent.BuyBudget!.Value * (1m + HundredBahtMandate.FeeReserveRate) <= 100m);
    }

    [Fact]
    public void Buy_over_hundred_baht_gross_is_rejected()
    {
        var intent = LiveRiskTests.Intent with { Quantity = 100.01m / 100000m, BuyBudget = 100.01m };
        var result = LiveRiskPolicy.Evaluate(intent, LiveRiskTests.Snapshot, HundredBahtMandate, LiveRiskTests.Gate, LiveRiskTests.Now);

        Assert.False(result.Allowed);
        Assert.Contains("MAX_ORDER_NOTIONAL_EXCEEDED", result.Violations);
    }

    [Fact]
    public void Pending_reservations_consume_available_cash()
    {
        var snapshot = LiveRiskTests.Snapshot with
        {
            PendingBuyNotional = 700m,
            CashReserved = 702.1m,
            PendingBuySymbols = ["BTC-THB"]
        };
        var result = LiveRiskPolicy.Evaluate(LiveRiskTests.Intent, snapshot, HundredBahtMandate, LiveRiskTests.Gate, LiveRiskTests.Now);

        Assert.False(result.Allowed);
        Assert.Contains("INSUFFICIENT_CASH", result.Violations);
    }

    [Fact]
    public void Sell_requires_owned_unreserved_lot_and_allows_reconciled_exit()
    {
        var quantity = decimal.Floor(99.99m / (1m + HundredBahtMandate.FeeReserveRate) / 100000m * 100000000m) / 100000000m;
        var intent = LiveRiskTests.Intent with { Side = OrderSide.Sell, Quantity = quantity, BuyBudget = null };
        var snapshot = LiveRiskTests.Snapshot with
        {
            CashBalance = 500m,
            Equity = 600m,
            Positions = [new("BTC-THB", .001m, 0m, 100000m)]
        };
        var result = LiveRiskPolicy.Evaluate(intent, snapshot, HundredBahtMandate, LiveRiskTests.Gate, LiveRiskTests.Now);

        Assert.True(result.Allowed, string.Join(",", result.Violations));
        Assert.False(LiveRiskPolicy.Evaluate(intent, snapshot with
        {
            Positions = [new("BTC-THB", .001m, .001m, 100000m)]
        }, HundredBahtMandate, LiveRiskTests.Gate, LiveRiskTests.Now).Allowed);
    }

    [Theory]
    [MemberData(nameof(Symbols))]
    public async Task Synthetic_roundtrip_reconciles_and_restores_without_duplicate_fills(string symbol, decimal price)
    {
        // Normalized synthetic evidence exercises accounting, not exchange-specific settlement conventions.
        var budgetQuantity = decimal.Floor(99m / price * 100000000m) / 100000000m;
        var budget = budgetQuantity * price;
        const decimal fee = .25m;
        var quantity = decimal.Floor((budget - fee) / price * 100000000m) / 100000000m;
        var notional = quantity * price;
        var quote = new LiveQuote(symbol, price, price, LiveRiskTests.Now);
        var state = new LiveAccountState(LiveRiskTests.Account, HundredBahtMandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot with { Quotes = [quote] }, [], [], [])
        {
            PerformanceBaseline = new(new(2026, 9, 27), 1000m, 1000m), FirstActivatedAt = LiveRiskTests.Now
        };
        LiveAccountTransaction Open() => new(state, (next, _) =>
        {
            state = JsonSerializer.Deserialize<LiveAccountState>(JsonSerializer.Serialize(next))!;
            return Task.CompletedTask;
        });
        var tx = Open();
        var buy = LiveRiskTests.Intent with { Symbol = symbol, LimitPrice = price, Quantity = budgetQuantity, BuyBudget = budget };
        Assert.Equal(LiveOrderState.Reserved, tx.Reserve(buy, LiveRiskTests.Now).State);
        tx.RecordTransition(buy.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        tx.RecordTransition(buy.Id, LiveOrderState.Acknowledged, "buy", LiveRiskTests.Now);
        await tx.CommitAsync(default);
        var buyFill = new BrokerFill(state.AccountId, "buy-fill", "buy", OrderSide.Buy, symbol, quantity, price, fee, 1);
        var buyOrder = new LiveBrokerOrder(buy.Id.ToString("N"), "buy", symbol, OrderSide.Buy,
            budgetQuantity, price, LiveOrderState.Filled, quantity, 0, 0) { BuyBudget = budget };
        var first = new LiveBrokerEvidence(state.AccountId, state.Mandate.CredentialFingerprint, LiveRiskTests.Now,
            true, 1000m - notional - fee, 0, [new(symbol, quantity, 0)], [buyOrder], [buyFill], [quote]);
        tx = Open();
        var purchase = tx.Reconcile(first, first, LiveRiskTests.Now);
        Assert.True(purchase.Complete, string.Join(",", purchase.Violations));
        await tx.CommitAsync(default);
        Assert.True(state.Gate.Armed, "A successful ordinary fill must not silently disable autonomous exits.");
        Assert.Equal(0m, state.Snapshot.CashReserved);
        Assert.Equal(quantity, Assert.Single(state.Snapshot.Positions).Quantity);
        Assert.Equal(LiveOrderState.Filled, Open().Reserve(buy, LiveRiskTests.Now).State);

        var sell = buy with { Id = Guid.NewGuid(), DecisionId = Guid.NewGuid(), Side = OrderSide.Sell,
            Quantity = quantity, BuyBudget = null, SnapshotVersion = state.Snapshot.Version };
        tx = Open();
        Assert.Equal(LiveOrderState.Reserved, tx.Reserve(sell, LiveRiskTests.Now).State);
        tx.RecordTransition(sell.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        tx.RecordTransition(sell.Id, LiveOrderState.Acknowledged, "sell", LiveRiskTests.Now);
        await tx.CommitAsync(default);
        var sellFill = new BrokerFill(state.AccountId, "sell-fill", "sell", OrderSide.Sell, symbol, quantity, price, fee, 1);
        var sellOrder = new LiveBrokerOrder(sell.Id.ToString("N"), "sell", symbol, OrderSide.Sell,
            quantity, price, LiveOrderState.Filled, quantity, 0, 0);
        var final = first with { CashBalance = 999.50m, Positions = [], Orders = [buyOrder, sellOrder], Fills = [buyFill, sellFill] };
        tx = Open();
        var sale = tx.Reconcile(final, final, LiveRiskTests.Now);
        Assert.True(sale.Complete, string.Join(",", sale.Violations));
        await tx.CommitAsync(default);
        tx = Open();
        Assert.True(tx.Reconcile(final, final, LiveRiskTests.Now).Complete);
        await tx.CommitAsync(default);
        Assert.Equal(999.50m, state.Snapshot.CashBalance);
        Assert.Equal(0m, state.Snapshot.CashReserved);
        Assert.Equal(2m * (notional + fee), state.Snapshot.DailyTurnover);
        Assert.Empty(state.Snapshot.Positions);
        Assert.Equal(2, state.Fills.Count);
        Assert.All(state.Orders, order => Assert.Equal(LiveOrderState.Filled, order.State));
        Assert.True(state.Gate.Armed);
    }
}
