using System.Text.Json;
using System.Text.Json.Nodes;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class AiCompanyTradePlanContractTests
{
    [Fact]
    public void PriceLevels_UsesTrailingWindowsAndTrueRange()
    {
        var symbol = new Symbol("BTC-THB");
        var bars = Enumerable.Range(0, 20).Select(i => new HistoricalBar(
            new DateOnly(2025, 1, 1).AddDays(i), symbol, 10m + i, 12m + i, 8m + i, 10m + i, 1)).ToArray();

        var levels = AiCompanyPriceLevels.From(bars);

        Assert.Equal(8m, levels.Low20);
        Assert.Equal(31m, levels.High20);
        Assert.Equal(4m, levels.AverageTrueRange14);
    }

    [Fact]
    public void Live_buy_has_a_numeric_plan_but_does_not_authorize_execution()
    {
        var result = Validate(Input(), Output());
        Assert.True(result.IsValid, string.Join(",", result.Violations));
        Assert.Equal(Plan, Assert.Single(result.Output!.Proposals).TradePlan);
    }

    [Fact]
    public void Forty_percent_allocation_includes_fee_reserve_before_quantity_rounding()
    {
        var input = Input() with { Portfolio = Input().Portfolio with { Equity = 4999.12m, Cash = 4999.12m },
            MaximumPositionWeight = 1, ExecutionLimits = Input().ExecutionLimits! with
            { CapitalAllocation = 5000.0872m, MaximumPositionWeight = 1, MaximumOrderNotional = 5000.0872m, FeeReserveRate = .0035m } };
        var plan = Plan with { EntryPrice = 83699.97m, Quantity = .02389m, TakeProfitPrice = 93000, StopLossPrice = 78897.11m };
        var output = JsonNode.Parse(Output("BUY", plan))!;
        output["proposals"]![0]!["targetWeight"] = .4m;
        Assert.Contains("TRADE_PLAN_BUDGET_EXCEEDED", Validate(input, output.ToJsonString()).Violations);
        var sizing = input.ExecutionLimits!.OrderSizing[0];
        var budget = Math.Min(input.Portfolio.Cash, Math.Min(input.ExecutionLimits.MaximumOrderNotional!.Value, .4m * input.Portfolio.Equity));
        var quantity = decimal.Floor(budget / (plan.EntryPrice * (1 + input.ExecutionLimits.FeeReserveRate)) / sizing.BaseStep) * sizing.BaseStep;
        Assert.Equal(.02380733m, quantity);
        output["proposals"]![0]!["tradePlan"]!["quantity"] = quantity;
        var corrected = Validate(input, output.ToJsonString());
        Assert.True(corrected.IsValid, string.Join(",", corrected.Violations));
    }

    [Theory]
    [InlineData("HOLD")]
    [InlineData("SELL")]
    [InlineData("TRIM")]
    public void Non_buy_accepts_null_plan_but_rejects_an_entry_plan(string action)
    {
        Assert.True(Validate(Input(), Output(action, null)).IsValid);
        Assert.Contains("TRADE_PLAN_ACTION_MISMATCH", Validate(Input(), Output(action)).Violations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Live_buy_requires_plan_for_both_missing_and_null(bool absent)
    {
        var json = Output("BUY", null);
        if (absent) json = json.Replace(",\"tradePlan\":null", "");
        Assert.Contains("MISSING_TRADE_PLAN", Validate(Input(), json).Violations);
        var paper = Input() with { AccountContext = null };
        Assert.True(Validate(paper, json).IsValid);
        Assert.Contains("TRADE_PLAN_ACTION_MISMATCH", Validate(paper, Output()).Violations);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("horizon")]
    [InlineData("no-zone")]
    [InlineData("non-iso")]
    [InlineData("stop")]
    [InlineData("quantity-step")]
    [InlineData("price-step")]
    [InlineData("fees")]
    [InlineData("budget")]
    [InlineData("cash")]
    [InlineData("ceiling")]
    [InlineData("dust")]
    [InlineData("overflow")]
    [InlineData("extra-field")]
    [InlineData("duplicate-field")]
    public void Untrusted_plan_fails_closed(string defect)
    {
        var input = Input();
        var plan = defect switch
        {
            "expired" => Plan with { ExpiresAt = LiveRiskTests.Now.AddMinutes(1) },
            "horizon" => Plan with { ExpiresAt = LiveRiskTests.Now.AddDays(7).AddSeconds(1) },
            "stop" => Plan with { StopLossPrice = Plan.EntryPrice },
            "quantity-step" => Plan with { Quantity = .001400001m },
            "price-step" => Plan with { TakeProfitPrice = 110000.001m },
            "fees" => Plan with { TakeProfitPrice = 100001m },
            "budget" => Plan with { Quantity = .002m },
            "dust" => Plan with { Quantity = .0001m },
            "overflow" => Plan with { EntryPrice = decimal.MaxValue / 2, TakeProfitPrice = decimal.MaxValue, Quantity = 100 },
            _ => Plan
        };
        if (defect == "cash") input = input with { Portfolio = input.Portfolio with { Cash = 10 } };
        if (defect == "ceiling") input = input with { ExecutionLimits = input.ExecutionLimits! with { MaximumOrderNotional = 100 } };
        var json = Output("BUY", plan);
        if (defect == "no-zone") json = json.Replace("2026-09-27T13:00:00+07:00", "2026-09-27T13:00:00");
        if (defect == "non-iso") json = json.Replace("2026-09-27T13:00:00+07:00", "September 27 2026 13:00:00+07:00");
        if (defect == "extra-field") json = json.Replace("\"entryPrice\":100000", "\"invented\":1,\"entryPrice\":100000");
        if (defect == "duplicate-field") json = json.Replace("\"entryPrice\":100000", "\"entryPrice\":100000,\"entryPrice\":100000");
        Assert.False(Validate(input, json).IsValid);
    }

    [Fact]
    public void Schema_requires_nullable_trade_plan_with_all_five_fields()
    {
        using var schema = JsonDocument.Parse(AiCompanyContract.OutputJsonSchema);
        var item = schema.RootElement.GetProperty("properties").GetProperty("proposals").GetProperty("items");
        Assert.Contains(item.GetProperty("required").EnumerateArray(), e => e.GetString() == "tradePlan");
        Assert.False(item.GetProperty("properties").GetProperty("tradePlan").GetProperty("anyOf")[1].GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void True_range_includes_gap_from_previous_close()
    {
        var bars = Enumerable.Range(0, 20).Select(i => new HistoricalBar(new DateOnly(2026, 1, 1).AddDays(i),
            new Symbol("BTC-THB"), i < 6 ? 10 : 22, i < 6 ? 12 : 24, i < 6 ? 8 : 20, i < 6 ? 10 : 22, 1)).ToArray();
        Assert.Equal(66m / 14, AiCompanyPriceLevels.From(bars).AverageTrueRange14);
    }

    private static readonly AiCompanyTradePlan Plan = new(100000, .0014m, 110000, 95000, LiveRiskTests.Now.AddHours(4));
    private static AiCompanyMeetingInput Input() => new(new(2026, 9, 26), new string('a', 64), new(1000, 1000, 0, 0, 0, 0),
        [new("btc-evidence", new("BTC-THB"), new(2026, 9, 26), 100000, false, 0, 0, 0, ForecastDirection.Neutral,
            StrategyKind.Cash, false, 0, 0)], "test", .2m, 3)
    { AccountContext = new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N")), ObservedAt = LiveRiskTests.Now,
        ExecutionLimits = new(1000, .2m, 3, .003m, .01m, ["BTC-THB"])
        { MaximumOrderNotional = 800, MaximumDailyTurnover = 2000, OrderSizing = [new("BTC-THB", .01m, .00000001m, 1, 10)] } };
    private static string Output(string action = "BUY") => Output(action, Plan);
    private static string Output(string action, AiCompanyTradePlan? plan) => JsonSerializer.Serialize(new
    {
        role = "portfolio-final", summary = "แผนทดสอบ", recommendations = new[] { "ทดสอบ" }, risks = new[] { "ราคา" },
        evidenceIds = new[] { "btc-evidence" }, dataCutoff = "2026-09-26", evidenceHash = new string('a', 64),
        proposals = new[] { new { action, symbol = "BTC-THB", targetWeight = .15m, confidence = .8m, thesis = "test", invalidation = "test", tradePlan = plan } }
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static AiCompanyOutputValidation Validate(AiCompanyMeetingInput input, string json) => AiCompanyContract.ParseAndValidate(input, "portfolio-final", json, true);
}
