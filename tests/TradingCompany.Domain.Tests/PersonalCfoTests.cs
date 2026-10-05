using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class PersonalCfoTests
{
    private static PersonalCfoMandate DemoMandate() => new(
        new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31), 30_000m, 12_000m, 0m,
        72_000m, .25m, .70m);

    [Theory]
    [InlineData(1_000, "PAUSED", 0)]
    [InlineData(2_999.99, "PAUSED", 0)]
    [InlineData(3_000, "LIMITED", .5)]
    [InlineData(8_999.99, "LIMITED", .5)]
    [InlineData(9_000, "FULL_BUDGET", 1)]
    public void Safety_gate_uses_exact_one_and_three_month_boundaries(decimal balance, string state, decimal multiplier)
    {
        var gate = CfoSafetyGate.Assess(DemoMandate() with { FixedEssentials = 3_000m }, balance);
        Assert.Equal(state, gate.State);
        Assert.Equal(multiplier, gate.InvestmentMultiplier);
    }

    [Fact]
    public void Gate_handles_zero_needs_and_deficit_at_opening()
    {
        var zero = CfoSafetyGate.Assess(DemoMandate() with { FixedEssentials = 0m, FixedDebtPayment = 0m }, 0m);
        Assert.Equal("FULL_BUDGET", zero.State);
        Assert.Null(zero.CoverageMonths);
        var deficit = DemoMandate() with { MonthlySalary = 1_000m, FixedEssentials = 800m, FixedDebtPayment = 500m };
        Assert.Equal("DEFICIT", CfoSafetyGate.Assess(deficit, 0m, true).State);
        Assert.Equal("DEFICIT", new PersonalCfoAllocationEngine().Run(deficit).OpeningSafetyGate.State);
    }

    [Fact]
    public void Initial_reserve_above_target_is_preserved()
    {
        var mandate = DemoMandate() with { End = new DateOnly(2024, 2, 29), InitialEmergencyBalance = 100_000m };
        var ledger = new PersonalCfoAllocationEngine().Run(mandate);
        Assert.Equal(100_000m, ledger.Months[0].OpeningEmergencyBalance);
        Assert.Equal(1m, ledger.Months[0].SafetyGate!.InvestmentMultiplier);
        Assert.Equal(0m, ledger.Months[0].Emergency);
        Assert.Equal(mandate.InitialEmergencyBalance + ledger.Months.Sum(x => x.Emergency), ledger.EmergencyBalance);
    }

    [Fact]
    public void User_budget_case_conserves_salary_and_redirects_limited_investment()
    {
        var mandate = new PersonalCfoMandate(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31), 74_000m, 24_400m, 25_000m, 120_000m, .25m, .7m, 120_000m);
        var ledger = new PersonalCfoAllocationEngine().Run(mandate);
        Assert.Equal(28_200m, ledger.OpeningSafetyGate.Shortfall);
        Assert.Equal(148_200m, ledger.EmergencyBalance);
        Assert.Equal(6_457.5m, ledger.Months[0].Investment);
        Assert.Equal(12_607.5m, ledger.Months[0].Emergency);
        Assert.Equal(5_535m, ledger.Months[0].Discretionary);
        Assert.Equal("FULL_BUDGET", ledger.Months[3].SafetyGate!.State);
        Assert.All(ledger.Months, row => Assert.Equal(row.Salary, row.Essentials + row.Debt + row.Emergency + row.Investment + row.Discretionary));
        Assert.Equal(ledger.Mandate.InitialEmergencyBalance + ledger.Months.Sum(x => x.Emergency), ledger.EmergencyBalance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2999.99)]
    public void Zero_target_and_rate_cannot_bypass_gate_and_promotion_waits_until_next_month(decimal opening)
    {
        var mandate = DemoMandate() with
        {
            MonthlySalary = 20_000m, FixedEssentials = 3_000m,
            EmergencyTarget = 0m, EmergencyRate = 0m, InvestingRate = 1m,
            InitialEmergencyBalance = opening, End = new DateOnly(2024, 2, 29)
        };
        var result = new PersonalCfoAllocationEngine().Run(mandate);
        var first = result.Months[0];
        Assert.Equal("PAUSED", first.SafetyGate!.State);
        Assert.Equal(0m, first.Investment);
        Assert.Equal(9_000m, first.EmergencyBalance);
        Assert.Equal(9_000m - opening, first.DivertedToEmergency);
        Assert.Equal(8_000m + opening, first.Discretionary);
        Assert.Equal("FULL_BUDGET", result.Months[1].SafetyGate!.State);
        Assert.Equal(17_000m, result.Months[1].Investment);
        Assert.Equal(new DateOnly(2024, 2, 1), Assert.Single(result.InvestmentSchedule).Date);
    }

    [Fact]
    public void Demo_mandate_conserves_every_month_and_releases_only_investment()
    {
        var result = new PersonalCfoAllocationEngine().Run(DemoMandate());
        Assert.Equal(24, result.Months.Count);
        Assert.All(result.Months, month => Assert.Equal(month.Salary,
            month.Essentials + month.Debt + month.Emergency + month.Investment + month.Discretionary));
        Assert.Equal(result.Months.Sum(x => x.Investment), result.InvestmentSchedule.Sum(x => x.Amount));
        Assert.Equal(72_000m, result.EmergencyBalance);
        Assert.All(result.Months.Where(x => x.Investment > 0), x => Assert.Contains(result.InvestmentSchedule, s => s.Date == x.SalaryDate && s.Amount == x.Investment));
    }

    [Fact]
    public void Underfunded_fixed_needs_release_zero_investment()
    {
        var result = new PersonalCfoAllocationEngine().Run(DemoMandate() with { MonthlySalary = 10_000m, FixedEssentials = 12_000m });
        Assert.All(result.Months, month => { Assert.Equal(TreasuryAllocationStatus.Deficit, month.Status); Assert.Equal(0m, month.Emergency); Assert.Equal(0m, month.Investment); Assert.Equal(0m, month.Discretionary); });
        Assert.Empty(result.InvestmentSchedule);
    }

    [Fact]
    public void Debt_is_funded_before_emergency_or_investment()
    {
        var m = new PersonalCfoAllocationEngine().Run(DemoMandate() with { FixedDebtPayment = 5_000m }).Months[0];
        Assert.Equal(5_000m, m.Debt); Assert.Equal(10_075m, m.Emergency); Assert.Equal(0m, m.Investment); Assert.Equal(2_925m, m.Discretionary); Assert.Equal(6_825m, m.DivertedToEmergency);
        Assert.Equal(TreasuryAllocationStatus.Funded, m.Status);
        Assert.Equal(m.Salary, m.Essentials + m.Debt + m.Emergency + m.Investment + m.Discretionary);
    }

    [Fact]
    public void Emergency_contribution_stops_exactly_at_target_and_rolls_forward_to_later_envelopes()
    {
        var result = new PersonalCfoAllocationEngine().Run(DemoMandate());
        Assert.Equal(72_000m, result.EmergencyBalance);
        Assert.All(result.Months, x => Assert.True(x.EmergencyBalance <= 72_000m));
        var targetIndex = result.Months.ToList().FindIndex(x => x.EmergencyBalance == 72_000m);
        Assert.True(targetIndex >= 0);
        var target = result.Months[targetIndex];
        var later = result.Months[targetIndex + 1];
        Assert.Equal(0m, later.Emergency);
        Assert.True(later.Investment > result.Months.First().Investment);
    }

    [Fact]
    public void Invalid_money_dates_and_rates_are_rejected()
    {
        var e = new PersonalCfoAllocationEngine();
        Assert.Throws<ArgumentException>(() => e.Run(DemoMandate() with { Start = new(2025, 1, 1), End = new(2024, 1, 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { MonthlySalary = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { FixedEssentials = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { FixedDebtPayment = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { EmergencyTarget = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { InitialEmergencyBalance = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { EmergencyRate = -.01m }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { EmergencyRate = 1.01m }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { InvestingRate = -.01m }));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Run(DemoMandate() with { InvestingRate = 1.01m }));
        Assert.Throws<ArgumentException>(() => e.Run(DemoMandate() with { End = new(2030, 1, 1) }));
    }

    [Fact]
    public void Ledger_collections_reject_mutation()
    {
        var ledger = new PersonalCfoAllocationEngine().Run(DemoMandate());
        Assert.Throws<NotSupportedException>(() => ((IList<MonthlyTreasuryAllocation>)ledger.Months)[0] = ledger.Months[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<ScheduledContribution>)ledger.InvestmentSchedule).Clear());
    }

    [Fact]
    public void Repeated_runs_return_equal_ledgers()
    {
        var engine = new PersonalCfoAllocationEngine();
        var first = engine.Run(DemoMandate());
        var second = engine.Run(DemoMandate());
        Assert.Equal(first.Months, second.Months);
        Assert.Equal(first.InvestmentSchedule, second.InvestmentSchedule);
    }
}
