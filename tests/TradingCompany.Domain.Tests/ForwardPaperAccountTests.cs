using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class ForwardPaperAccountTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-09-30T10:00:00Z");
    private static ForwardPaperAccount Funded(decimal amount = 12000m) => ForwardPaperLedger.Fund(
        ForwardPaperLedger.Open(Guid.NewGuid()), new(new DateOnly(2026, 9, 30), amount, Day));
    private static ForwardPaperIntent Order(OrderSide side, decimal quantity = 2m) =>
        new(Guid.NewGuid(), "NVDA", side, quantity, Day.AddMinutes(1), new());
    private static ForwardPaperQuote Quote(decimal price = 100m) =>
        new("NVDA", price, "USD", 35m, Day.AddMinutes(2), Day.AddMinutes(2), Day, "controlled-fixture", 10m);

    [Fact]
    public void Buy_and_sell_reconcile_fx_fees_and_contributions()
    {
        // 40,000 keeps the 7,010 purchase within the unchanged 20% risk limit.
        var account = Funded(40000m);
        var buy = Order(OrderSide.Buy);
        account = ForwardPaperLedger.Submit(account, buy, Day.AddMinutes(1));
        account = ForwardPaperLedger.Execute(account, buy.Id, Quote(), Day.AddMinutes(3));
        var first = ForwardPaperLedger.Snapshot(account);
        Assert.Equal(32990m, first.Cash);
        Assert.Equal(39990m, first.Equity);
        Assert.Equal(-10m, first.TotalPnl);
        Assert.Equal(2m, Assert.Single(first.Positions).Quantity);
        var sell = Order(OrderSide.Sell) with { DecisionAt = Day.AddMinutes(4) };
        account = ForwardPaperLedger.Submit(account, sell, Day.AddMinutes(4));
        account = ForwardPaperLedger.Execute(account, sell.Id,
            Quote(110m) with { ObservedAt = Day.AddMinutes(5), AvailableAt = Day.AddMinutes(5) }, Day.AddMinutes(6));
        var last = ForwardPaperLedger.Snapshot(account);
        Assert.Equal(40680m, last.Cash);
        Assert.Equal(680m, last.TotalPnl);
        Assert.Equal(680m, last.RealizedPnl);
        Assert.Empty(last.Positions);
        Assert.Empty(last.PendingOrders);
    }

    [Fact]
    public void Risk_rejects_oversize_order_without_spending_reserve_or_cash()
    {
        var account = Funded(); var order = Order(OrderSide.Buy);
        account = ForwardPaperLedger.Submit(account, order, Day.AddMinutes(1));
        account = ForwardPaperLedger.Execute(account, order.Id, Quote(), Day.AddMinutes(3));
        var snapshot = ForwardPaperLedger.Snapshot(account);
        Assert.Equal(12000m, snapshot.Cash);
        Assert.Empty(snapshot.Positions);
        Assert.Contains("MAX_ORDER_NOTIONAL_EXCEEDED", Assert.Single(snapshot.Receipts).Violations);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("old")]
    [InlineData("future")]
    [InlineData("currency")]
    [InlineData("fx")]
    [InlineData("symbol")]
    [InlineData("stale")]
    public void Unusable_quote_leaves_order_pending_and_account_unchanged(string mode)
    {
        var account = Funded(40000m); var order = Order(OrderSide.Buy);
        account = ForwardPaperLedger.Submit(account, order, Day.AddMinutes(1));
        var quote = mode switch
        {
            "missing" => null,
            "old" => Quote() with { ObservedAt = Day },
            "future" => Quote() with { AvailableAt = Day.AddDays(1) },
            "currency" => Quote() with { Currency = "EUR" },
            "fx" => Quote() with { ThbPerUnit = 0m },
            "symbol" => Quote() with { Symbol = "AMD" },
            _ => Quote()
        };
        Assert.Equal(account, ForwardPaperLedger.Execute(account, order.Id, quote,
            mode == "stale" ? Day.AddDays(1) : Day.AddMinutes(3)));
    }

    [Fact]
    public void Identical_retries_are_noops_but_reused_id_with_other_payload_is_rejected()
    {
        var account = Funded(40000m); var order = Order(OrderSide.Buy);
        account = ForwardPaperLedger.Submit(account, order, Day.AddMinutes(1));
        Assert.Equal(account, ForwardPaperLedger.Submit(account, order, Day.AddMinutes(2)));
        Assert.Throws<InvalidDataException>(() => ForwardPaperLedger.Submit(account, order with { Quantity = 1 }, Day.AddMinutes(2)));
        account = ForwardPaperLedger.Execute(account, order.Id, Quote(), Day.AddMinutes(3));
        Assert.Equal(account, ForwardPaperLedger.Execute(account, order.Id, Quote(200), Day.AddMinutes(4)));
    }

    [Fact]
    public void Contribution_retries_do_not_create_profit_and_short_selling_is_rejected()
    {
        var account = Funded();
        var credit = account.Events[0].Credit!;
        Assert.Equal(account, ForwardPaperLedger.Fund(account, credit));
        Assert.Throws<InvalidDataException>(() => ForwardPaperLedger.Fund(account, credit with { Amount = 999m }));
        var sell = Order(OrderSide.Sell);
        account = ForwardPaperLedger.Submit(account, sell, Day.AddMinutes(1));
        account = ForwardPaperLedger.Execute(account, sell.Id, Quote(), Day.AddMinutes(3));
        Assert.Equal(0m, ForwardPaperLedger.Snapshot(account).TotalPnl);
        Assert.Contains("INSUFFICIENT_POSITION", ForwardPaperLedger.Snapshot(account).Receipts[0].Violations);
    }

    [Fact]
    public void Altered_saved_receipt_fails_reconciliation()
    {
        var account = Funded(); var order = Order(OrderSide.Buy);
        account = ForwardPaperLedger.Submit(account, order, Day.AddMinutes(1));
        account = ForwardPaperLedger.Execute(account, order.Id, Quote(), Day.AddMinutes(3));
        var events = account.Events.ToArray();
        events[^1] = events[^1] with { Fill = events[^1].Fill! with { Status = "PAPER_FILLED", Violations = [] } };
        Assert.Throws<InvalidDataException>(() => ForwardPaperLedger.Validate(account with { Events = events }));
    }

    [Fact]
    public void Fresh_valuation_updates_unrealized_pnl_and_drawdown_without_a_trade()
    {
        var account = Funded(40000m); var buy = Order(OrderSide.Buy);
        account = ForwardPaperLedger.Submit(account, buy, Day.AddMinutes(1));
        account = ForwardPaperLedger.Execute(account, buy.Id, Quote(), Day.AddMinutes(3));
        account = ForwardPaperLedger.Mark(account, Quote(90) with
            { ObservedAt = Day.AddMinutes(4), AvailableAt = Day.AddMinutes(4) }, Day.AddMinutes(5));
        var state = ForwardPaperLedger.Snapshot(account);
        Assert.Equal(32990m, state.Cash);
        Assert.Equal(39290m, state.Equity);
        Assert.Equal(-710m, state.TotalPnl);
        Assert.Equal(710m, state.Drawdown);
        Assert.Single(state.Receipts);
    }

    [Fact]
    public void New_buy_cannot_use_stale_valuations_of_existing_holdings()
    {
        var account = Funded(40000m); var buy = Order(OrderSide.Buy);
        account = ForwardPaperLedger.Submit(account, buy, Day.AddMinutes(1));
        account = ForwardPaperLedger.Execute(account, buy.Id, Quote(), Day.AddMinutes(3));
        var second = Order(OrderSide.Buy) with { Symbol = "MSFT", DecisionAt = Day.AddDays(1) };
        account = ForwardPaperLedger.Submit(account, second, second.DecisionAt);
        account = ForwardPaperLedger.Execute(account, second.Id, Quote() with { Symbol = "MSFT",
            ObservedAt = Day.AddDays(1).AddMinutes(1), AvailableAt = Day.AddDays(1).AddMinutes(1) }, Day.AddDays(1).AddMinutes(2));
        Assert.Equal("RISK_REJECTED", ForwardPaperLedger.Snapshot(account).Receipts[^1].Status);
        Assert.Contains("STALE_POSITION_VALUATION", ForwardPaperLedger.Snapshot(account).Receipts[^1].Violations);
    }
}
