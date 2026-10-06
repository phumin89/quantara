using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubRoundedBuyTests
{
    [Fact]
    public void Floor_buy_rounds_received_base_and_preserves_confirmed_gross()
    {
        var fill = BitkubReconciliationSource.Normalize(LiveRiskTests.Account,
            BudgetIntent(), "order", RawFill(), Rules(BitkubBuyBaseRounding.FloorToBaseStep));

        Assert.Equal(33.33m, fill.Quantity);
        Assert.Equal(100m, fill.GrossQuoteAmount);
        Assert.Equal(0m, fill.CashFee);
    }

    [Fact]
    public void Exact_rounding_rejects_nondivisible_received_base()
        => Assert.Throws<InvalidDataException>(() => BitkubReconciliationSource.Normalize(
            LiveRiskTests.Account, BudgetIntent(), "order", RawFill(), Rules(BitkubBuyBaseRounding.Exact)));

    [Fact]
    public void Zero_or_unknown_rounding_mode_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => BitkubReconciliationSource.Normalize(
            LiveRiskTests.Account, BudgetIntent(), "order", RawFill(), Rules(BitkubBuyBaseRounding.FloorToBaseStep) with
            { Pairs = [Pair(BitkubBuyBaseRounding.FloorToBaseStep) with { SellBaseStep = 0m }] }));
        Assert.Throws<InvalidDataException>(() => BitkubExecutionAdapter.ValidateRules(Rules((BitkubBuyBaseRounding)99)));
    }

    [Fact]
    public void Budget_fill_uses_explicit_gross_for_cash_and_turnover_and_is_idempotent()
    {
        var tx = Seed();
        tx.ApplyFill(new BrokerFill(LiveRiskTests.Account, "tx", "order", OrderSide.Buy, "BTC-THB",
            33.33m, 3m, .25m, 1) { QuoteAmount = 100m }, LiveRiskTests.Now);
        Assert.Equal(899.75m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(100.25m, tx.Account.Snapshot.DailyTurnover);
        Assert.Equal(33.33m, Assert.Single(tx.Account.Snapshot.Positions).Quantity);
        tx.ApplyFill(new BrokerFill(LiveRiskTests.Account, "tx", "order", OrderSide.Buy,
            "BTC-THB", 33.33m, 3m, .25m, 1) { QuoteAmount = 100m }, LiveRiskTests.Now);
        Assert.Equal(899.75m, tx.Account.Snapshot.CashBalance);
    }

    [Fact]
    public void Fee_correction_refunds_cash_without_adding_quantity_and_bad_notional_is_atomic()
    {
        var tx = Seed();
        var fill = new BrokerFill(LiveRiskTests.Account, "tx", "order", OrderSide.Buy, "BTC-THB",
            33.33m, 3m, .25m, 1) { QuoteAmount = 100m };
        tx.ApplyFill(fill, LiveRiskTests.Now);
        tx.ApplyFill(fill with { CashFee = .20m, Revision = 2 }, LiveRiskTests.Now);
        Assert.Equal(899.80m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(33.33m, Assert.Single(tx.Account.Snapshot.Positions).Quantity);
        var before = tx.Account;
        Assert.Throws<InvalidDataException>(() => tx.ApplyFill(fill with { QuoteAmount = 100.01m, Revision = 3 }, LiveRiskTests.Now));
        Assert.Equal(before, tx.Account);
        Assert.Throws<InvalidDataException>(() => tx.ApplyFill(fill with { QuoteAmount = -1m, Revision = 3 }, LiveRiskTests.Now));
        Assert.Equal(before, tx.Account);
    }

    [Fact]
    public void Floor_rounding_rejects_zero_result()
    {
        var raw = RawFill() with { RawAmount = .001m };
        Assert.Throws<InvalidDataException>(() => BitkubReconciliationSource.Normalize(
            LiveRiskTests.Account, BudgetIntent(), "order", raw, Rules(BitkubBuyBaseRounding.FloorToBaseStep)));
    }

    [Fact]
    public void Budget_rejects_overbudget_and_quote_mismatches_without_mutation()
    {
        var tx = Seed();
        var fill = new BrokerFill(LiveRiskTests.Account, "tx", "order", OrderSide.Buy, "BTC-THB", 33.33m, 3m, .25m, 1) { QuoteAmount = 100m };
        var before = tx.Account;
        Assert.Throws<InvalidDataException>(() => tx.ApplyFill(fill with { QuoteAmount = 103m }, LiveRiskTests.Now));
        Assert.Equal(before, tx.Account);
        Assert.Throws<InvalidDataException>(() => tx.ApplyFill(fill with { QuoteAmount = 99m }, LiveRiskTests.Now));
        Assert.Equal(before, tx.Account);
    }

    [Fact]
    public void Explicit_quote_is_supported_only_for_budget_buys()
    {
        var tx = Seed(LiveRiskTests.Intent with { Side = OrderSide.Sell, BuyBudget = null, Quantity = 1m, LimitPrice = 3m });
        var sell = new BrokerFill(LiveRiskTests.Account, "sell-tx", "order", OrderSide.Sell, "BTC-THB", 1m, 3m, 0m, 1) { QuoteAmount = 3m };
        Assert.Equal("Unsupported broker quote amount.", Assert.Throws<InvalidDataException>(() => tx.ApplyFill(sell, LiveRiskTests.Now)).Message);
        var nonBudget = Seed(LiveRiskTests.Intent with { BuyBudget = null, Quantity = 34m, LimitPrice = 3m });
        Assert.Equal("Unsupported broker quote amount.", Assert.Throws<InvalidDataException>(() => nonBudget.ApplyFill(
            sell with { Side = OrderSide.Buy }, LiveRiskTests.Now)).Message);
    }

    [Fact]
    public void Legacy_fill_json_without_quote_amount_uses_quantity_times_price()
    {
        var old = "{\"AccountId\":{\"Value\":\"" + LiveRiskTests.Account.Value + "\"},\"TransactionId\":\"tx\",\"OrderId\":\"order\",\"Side\":0,\"Symbol\":\"BTC-THB\",\"Quantity\":33.33,\"Price\":3,\"CashFee\":0,\"Revision\":1}";
        var fill = JsonSerializer.Deserialize<BrokerFill>(old)!;
        Assert.Equal(99.99m, fill.GrossQuoteAmount);
    }

    private static LiveIntent BudgetIntent() => LiveRiskTests.Intent with { BuyBudget = 100m };
    private static BrokerOrderFillEvidence RawFill() => new("order", "tx", 3m, 100m, 0m, 0m, LiveRiskTests.Now.ToUnixTimeMilliseconds());
    private static BitkubPairExecutionRules Pair(BitkubBuyBaseRounding mode) => new("BTC-THB", 1m, .01m, 1m, 10m, 8, .00000001m) { BuyBaseRounding = mode };
    private static BitkubVerifiedExecutionRules Rules(BitkubBuyBaseRounding mode) => new("confirm", new string('a', 64), .0025m, BitkubBuyHistoryAmount.QuoteBeforeCashFee, [Pair(mode)]);
    private static LiveAccountTransaction Seed(LiveIntent? supplied = null)
    {
        var intent = supplied ?? (LiveRiskTests.Intent with { BuyBudget = 102m, Quantity = 34m, LimitPrice = 3m });
        var order = new LiveOrderRecord(intent, LiveOrderState.Acknowledged, "order", 102m, 0m, 0m,
            "hash", LiveRiskTests.Now, []);
        return new LiveAccountTransaction(new(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot, [order], [], []), (_, _) => Task.CompletedTask);
    }
}
