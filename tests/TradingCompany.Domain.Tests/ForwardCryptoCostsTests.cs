using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class ForwardCryptoCostsTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-09-25T10:00:00Z");
    private static ForwardPaperAccount Funded() => ForwardPaperLedger.Fund(ForwardPaperLedger.Open(Guid.NewGuid()), new(new(2026, 9, 25), 10000m, Day));
    private static ForwardPaperIntent Buy() => new(Guid.NewGuid(), "ETH-THB", OrderSide.Buy, .01m, Day.AddMinutes(1), new());
    private static ForwardPaperQuote Quote() => new("ETH-THB", 100000m, "THB", 1m,
        Day.AddMinutes(2), Day.AddMinutes(2), Day.AddMinutes(2), "fixture-depth", 0m, 100m, .0025m, .02m);

    [Fact]
    public void Percentage_fees_and_spread_reconcile_on_both_sides()
    {
        var buy = Buy();
        var account = ForwardPaperLedger.Submit(Funded(), buy, buy.DecisionAt);
        account = ForwardPaperLedger.Execute(account, buy.Id, Quote(), Day.AddMinutes(2));
        var bought = ForwardPaperLedger.Snapshot(account);
        Assert.Equal(8996.4975m, bought.Cash); // 1001 notional + 2.5025 fee
        Assert.Equal(-3.5025m, bought.TotalPnl);
        var sell = buy with { Id = Guid.NewGuid(), Side = OrderSide.Sell, DecisionAt = Day.AddMinutes(3) };
        account = ForwardPaperLedger.Submit(account, sell, sell.DecisionAt);
        var quote = Quote() with { ObservedAt = Day.AddMinutes(4), AvailableAt = Day.AddMinutes(4) };
        account = ForwardPaperLedger.Execute(account, sell.Id, quote, Day.AddMinutes(4));
        var sold = ForwardPaperLedger.Snapshot(account);
        Assert.Equal(9993m, sold.Cash); // 999 notional - 2.4975 fee
        Assert.Equal(-7m, sold.RealizedPnl);
        Assert.Equal(-7m, sold.TotalPnl);
        Assert.Empty(sold.Positions);
    }

    [Fact]
    public void Order_larger_than_observed_depth_is_rejected_without_spending()
    {
        var buy = Buy();
        var account = ForwardPaperLedger.Submit(Funded(), buy, buy.DecisionAt);
        account = ForwardPaperLedger.Execute(account, buy.Id, Quote() with { MaximumQuantity = .005m }, Day.AddMinutes(2));
        var result = ForwardPaperLedger.Snapshot(account);
        Assert.Equal(10000m, result.Cash);
        Assert.Contains("INSUFFICIENT_QUOTED_DEPTH", Assert.Single(result.Receipts).Violations);
    }

    [Fact]
    public void Fee_is_included_in_the_existing_order_risk_ceiling()
    {
        var buy = Buy() with { Quantity = .02m };
        var account = ForwardPaperLedger.Submit(Funded(), buy, buy.DecisionAt);
        account = ForwardPaperLedger.Execute(account, buy.Id, Quote() with { SlippageThbPerUnit = 0 }, Day.AddMinutes(2));
        Assert.Contains("MAX_ORDER_NOTIONAL_EXCEEDED", Assert.Single(ForwardPaperLedger.Snapshot(account).Receipts).Violations);
    }

    [Theory]
    [InlineData(-.01, .02)]
    [InlineData(1.01, .02)]
    [InlineData(.0025, 0)]
    public void Invalid_fee_or_depth_never_executes(decimal rate, decimal depth)
    {
        var buy = Buy();
        var account = ForwardPaperLedger.Submit(Funded(), buy, buy.DecisionAt);
        Assert.Same(account, ForwardPaperLedger.Execute(account, buy.Id, Quote() with { FeeRate = rate, MaximumQuantity = depth }, Day.AddMinutes(2)));
    }

    [Fact]
    public void Old_quote_serialization_is_unchanged_when_new_fields_are_absent()
    {
        const string old = "{\"symbol\":\"NVDA\",\"nativePrice\":100,\"currency\":\"USD\",\"thbPerUnit\":35,\"observedAt\":\"2026-09-25T10:00:00+00:00\",\"availableAt\":\"2026-09-25T10:00:00+00:00\",\"fxAt\":\"2026-09-25T10:00:00+00:00\",\"source\":\"old-fixture\",\"feeThb\":0,\"slippageThbPerUnit\":0}";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(old, JsonSerializer.Serialize(JsonSerializer.Deserialize<ForwardPaperQuote>(old, options), options));
    }
}
