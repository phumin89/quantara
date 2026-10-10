using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class FullPortfolioPolicyTests
{
    private static LiveMandate Full => LiveRiskTests.Mandate with
    {
        UseFullPortfolio = true, MaximumOrderNotional = 1000, MaximumDailyTurnover = 1000,
        MaximumDailyLoss = 1000, MaximumPortfolioExposure = 1, MaximumDrawdown = 1
    };

    [Theory]
    [InlineData(2000, .019)]
    [InlineData(500, .0049)]
    public void Buying_power_follows_verified_equity_without_mutating_owner_grant(decimal equity, decimal quantity)
    {
        var snapshot = LiveRiskTests.Snapshot with { CashBalance = equity, Equity = equity };
        var intent = LiveRiskTests.Intent with { Quantity = quantity };
        Assert.True(LiveRiskPolicy.Evaluate(intent, snapshot, Full, LiveRiskTests.Gate, LiveRiskTests.Now).Allowed);
        Assert.Equal(1000, Full.CapitalAllocation);
        Assert.Equal(equity, LiveRiskPolicy.PortfolioLimits(Full, snapshot).CapitalAllocation);
        if (equity > 1000) Assert.False(LiveRiskPolicy.Evaluate(intent, snapshot, Full with { UseFullPortfolio = false }, LiveRiskTests.Gate, LiveRiskTests.Now).Allowed);
    }

    [Fact]
    public void Verified_settlement_can_recycle_cash_without_erasing_daily_loss_or_turnover()
    {
        var snapshot = LiveRiskTests.Snapshot with { CashBalance = 400, Equity = 400, DailyLoss = 600, DailyTurnover = 20000, Drawdown = .6m };
        Assert.True(LiveRiskPolicy.Evaluate(LiveRiskTests.Intent with { Quantity = .003m }, snapshot, Full, LiveRiskTests.Gate, LiveRiskTests.Now).Allowed);
        Assert.Equal(1000, LiveRiskPolicy.PortfolioLimits(Full, snapshot).MaximumDailyLoss);
        Assert.Equal(600, snapshot.DailyLoss);
        Assert.Equal(20000, snapshot.DailyTurnover);
        Assert.Contains("DAILY_TURNOVER_EXCEEDED", LiveRiskPolicy.Evaluate(LiveRiskTests.Intent, snapshot, Full with { UseFullPortfolio = false }, LiveRiskTests.Gate, LiveRiskTests.Now).Violations);
    }

    [Theory]
    [InlineData("fees", "INSUFFICIENT_CASH")]
    [InlineData("reserved", "INSUFFICIENT_CASH")]
    [InlineData("stale", "STALE_SNAPSHOT")]
    [InlineData("reconciliation", "RECONCILIATION_REQUIRED")]
    [InlineData("stop", "STOPPED")]
    [InlineData("disarmed", "NOT_ARMED")]
    [InlineData("binding", "ACCOUNT_BINDING_CHANGED")]
    public void Full_portfolio_does_not_bypass_real_money_gates(string defect, string reason)
    {
        var snapshot = LiveRiskTests.Snapshot;
        var gate = LiveRiskTests.Gate;
        var intent = LiveRiskTests.Intent;
        switch (defect)
        {
            case "fees": intent = intent with { Quantity = .01m }; break;
            case "reserved": snapshot = snapshot with { CashReserved = 900, PendingBuyNotional = 900 }; break;
            case "stale": snapshot = snapshot with { ObservedAt = LiveRiskTests.Now.AddSeconds(-31) }; break;
            case "reconciliation": snapshot = snapshot with { Reconciled = false }; break;
            case "stop": gate = gate with { Stopped = true }; break;
            case "disarmed": gate = gate with { Armed = false }; break;
            case "binding": gate = gate with { CredentialFingerprint = "other" }; break;
        }
        var result = LiveRiskPolicy.Evaluate(intent, snapshot, Full, gate, LiveRiskTests.Now);
        Assert.False(result.Allowed);
        Assert.Contains(reason, result.Violations);
    }

    [Fact]
    public void Selling_all_owned_inventory_at_profit_is_not_limited_to_initial_capital()
    {
        var snapshot = LiveRiskTests.Snapshot with { CashBalance = 0, Equity = 2000, DailyTurnover = 10000,
            Positions = [new("BTC-THB", .02m, 0, 100000m)] };
        var intent = LiveRiskTests.Intent with { Side = OrderSide.Sell, Quantity = .02m, LimitPrice = 99900m };
        Assert.True(LiveRiskPolicy.Evaluate(intent, snapshot, Full, LiveRiskTests.Gate, LiveRiskTests.Now).Allowed);
        Assert.Contains("INSUFFICIENT_OWNED_QUANTITY", LiveRiskPolicy.Evaluate(intent with { Quantity = .021m }, snapshot, Full, LiveRiskTests.Gate, LiveRiskTests.Now).Violations);
    }

    [Fact]
    public void Entry_exit_plan_can_use_current_portfolio_and_recycle_settled_capital()
    {
        var snapshot = LiveRiskTests.Snapshot with { CashBalance = 2000, Equity = 2000, DailyTurnover = 10000 };
        var account = new LiveAccountState(LiveRiskTests.Account, Full, LiveRiskTests.Gate, snapshot, [], [], [])
            { PerformanceBaseline = new(new(2026, 9, 27), 2000, 2000) };
        var decision = Guid.NewGuid();
        var request = new LiveTradePlanRequest(LiveRiskTests.Account, LiveTradePlans.PlanId(decision, "BTC-THB"), decision,
            1, 1, "BTC-THB", new string('a', 64), new(100000, .018m, 120000, 90000, LiveRiskTests.Now.AddHours(1)));
        var sizing = new LiveOrderSizing("BTC-THB", 1, .00000001m, .01m, 10);
        Assert.Empty(LiveTradePlans.Validate(account, request, sizing, LiveRiskTests.Now));
        Assert.Contains("PLAN_ROUND_TRIP_TURNOVER_EXCEEDED", LiveTradePlans.Validate(account with
            { Mandate = Full with { UseFullPortfolio = false } }, request, sizing, LiveRiskTests.Now));
    }

    [Fact]
    public void Legacy_saved_mandate_stays_fixed_and_explicit_grant_survives_serialization()
    {
        var legacy = JsonSerializer.Deserialize<LiveMandate>(JsonSerializer.Serialize(LiveRiskTests.Mandate))!;
        Assert.False(legacy.UseFullPortfolio);
        Assert.Equal(legacy, LiveRiskPolicy.PortfolioLimits(legacy, LiveRiskTests.Snapshot with { Equity = 2000 }));
        Assert.True(JsonSerializer.Deserialize<LiveMandate>(JsonSerializer.Serialize(Full))!.UseFullPortfolio);
        Assert.False(LiveRiskPolicy.IsValidMandate(Full with { MaximumPortfolioExposure = .5m }));
        Assert.False(LiveRiskPolicy.IsValidMandate(Full with { MaximumOrderNotional = 100 }));
    }
}
