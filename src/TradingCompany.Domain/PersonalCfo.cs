namespace TradingCompany.Domain;

public enum TreasuryAllocationStatus { Funded, Deficit }

public sealed record PersonalCfoMandate(
    DateOnly Start, DateOnly End, decimal MonthlySalary, decimal FixedEssentials,
    decimal FixedDebtPayment, decimal EmergencyTarget, decimal EmergencyRate,
    decimal InvestingRate, decimal InitialEmergencyBalance = 0m);

public sealed record CfoSafetyAssessment(
    string State, decimal? CoverageMonths, decimal RequiredMonthlyExpenses,
    decimal MinimumReserve, decimal EffectiveTarget, decimal Shortfall,
    decimal InvestmentMultiplier, string Reason);

public static class CfoSafetyGate
{
    public static CfoSafetyAssessment Assess(PersonalCfoMandate mandate, decimal balance, bool deficit = false)
    {
        ArgumentNullException.ThrowIfNull(mandate);
        if (balance < 0) throw new ArgumentOutOfRangeException(nameof(balance));

        var needs = mandate.FixedEssentials + mandate.FixedDebtPayment;
        var minimum = 3m * needs;
        var target = Math.Max(mandate.EmergencyTarget, minimum);
        var shortfall = Math.Max(0m, minimum - balance);
        decimal? coverage = needs == 0 ? null : balance / needs;

        if (deficit)
            return new("DEFICIT", coverage, needs, minimum, target, shortfall, 0m,
                "Fixed needs exceed this month's salary, so investing is paused.");
        if (needs == 0)
            return new("FULL_BUDGET", null, needs, minimum, target, shortfall, 1m,
                "No fixed needs are required, so the full budget is available.");
        if (balance < needs)
            return new("PAUSED", coverage, needs, minimum, target, shortfall, 0m,
                "Emergency reserves cover less than one month of fixed needs, so investing is paused.");
        if (balance < minimum)
            return new("LIMITED", coverage, needs, minimum, target, shortfall, .5m,
                "Emergency reserves cover one to under three months of fixed needs; 50% of the planned investment budget is available.");
        return new("FULL_BUDGET", coverage, needs, minimum, target, shortfall, 1m,
            "Emergency reserves cover at least three months of fixed needs, so the full budget is available.");
    }
}

public sealed record MonthlyTreasuryAllocation(
    DateOnly SalaryDate, decimal Salary, decimal Essentials, decimal Debt, decimal Emergency,
    decimal Investment, decimal Discretionary, decimal EmergencyBalance, TreasuryAllocationStatus Status)
{
    public CfoSafetyAssessment? SafetyGate { get; init; }
    public decimal OpeningEmergencyBalance { get; init; }
    public decimal DivertedToEmergency { get; init; }
}

public sealed record ScheduledContribution(DateOnly Date, decimal Amount);

public sealed record PersonalCfoLedger(
    PersonalCfoMandate Mandate,
    IReadOnlyList<MonthlyTreasuryAllocation> Months,
    IReadOnlyList<ScheduledContribution> InvestmentSchedule)
{
    public decimal TotalSalary => Months.Sum(x => x.Salary);
    public decimal TotalEssentials => Months.Sum(x => x.Essentials);
    public decimal TotalDebt => Months.Sum(x => x.Debt);
    public decimal EmergencyBalance => Months.Count == 0 ? Mandate.InitialEmergencyBalance : Months[^1].EmergencyBalance;
    public decimal TotalInvestmentReleased => InvestmentSchedule.Sum(x => x.Amount);
    public decimal TotalDiscretionary => Months.Sum(x => x.Discretionary);
    public CfoSafetyAssessment OpeningSafetyGate => CfoSafetyGate.Assess(
        Mandate, Mandate.InitialEmergencyBalance, Mandate.MonthlySalary < Mandate.FixedEssentials + Mandate.FixedDebtPayment);
    public CfoSafetyAssessment ClosingSafetyGate => Months.Count == 0 ? OpeningSafetyGate
        : CfoSafetyGate.Assess(Mandate, EmergencyBalance, Months[^1].Status == TreasuryAllocationStatus.Deficit);
}

public sealed class PersonalCfoAllocationEngine
{
    public PersonalCfoLedger Run(PersonalCfoMandate mandate)
    {
        ArgumentNullException.ThrowIfNull(mandate);
        if (mandate.Start > mandate.End) throw new ArgumentException("Start must not exceed End.");
        var cycleCount = (mandate.End.Year - mandate.Start.Year) * 12 + mandate.End.Month - mandate.Start.Month + 1;
        if (cycleCount > 60) throw new ArgumentException("Mandate contains more than 60 months.");
        if (mandate.MonthlySalary <= 0 || mandate.FixedEssentials < 0 || mandate.FixedDebtPayment < 0
            || mandate.EmergencyTarget < 0 || mandate.InitialEmergencyBalance < 0)
            throw new ArgumentOutOfRangeException(nameof(mandate));
        if (mandate.EmergencyRate is < 0 or > 1 || mandate.InvestingRate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(mandate));

        var months = new List<MonthlyTreasuryAllocation>();
        var schedule = new List<ScheduledContribution>();
        var balance = mandate.InitialEmergencyBalance;
        var month = new DateOnly(mandate.Start.Year, mandate.Start.Month, 1);
        while (month <= mandate.End)
        {
            var opening = balance;
            var essentials = Math.Min(mandate.MonthlySalary, mandate.FixedEssentials);
            var remaining = mandate.MonthlySalary - essentials;
            var debt = Math.Min(remaining, mandate.FixedDebtPayment);
            remaining -= debt;
            var deficit = essentials < mandate.FixedEssentials || debt < mandate.FixedDebtPayment;
            // Decide against existing reserves; this month's deposits only unlock a later cycle.
            var gate = CfoSafetyGate.Assess(mandate, opening, deficit);
            var baseEmergency = deficit ? 0m : Math.Min(
                decimal.Round(remaining * mandate.EmergencyRate, 2, MidpointRounding.AwayFromZero),
                Math.Max(0m, gate.EffectiveTarget - opening));
            var proposedInvestment = deficit ? 0m
                : decimal.Round((remaining - baseEmergency) * mandate.InvestingRate, 2, MidpointRounding.AwayFromZero);
            var investment = decimal.Round(proposedInvestment * gate.InvestmentMultiplier, 2, MidpointRounding.AwayFromZero);
            var diverted = Math.Min(proposedInvestment - investment,
                Math.Max(0m, gate.EffectiveTarget - opening - baseEmergency));
            var emergency = baseEmergency + diverted;
            balance += emergency;
            var discretionary = remaining - emergency - investment;

            months.Add(new MonthlyTreasuryAllocation(
                month, mandate.MonthlySalary, essentials, debt, emergency, investment, discretionary, balance,
                deficit ? TreasuryAllocationStatus.Deficit : TreasuryAllocationStatus.Funded)
            {
                SafetyGate = gate,
                OpeningEmergencyBalance = opening,
                DivertedToEmergency = diverted
            });
            if (investment > 0) schedule.Add(new ScheduledContribution(month, investment));
            month = month.AddMonths(1);
        }
        ValidateLedger(months, schedule, mandate.InitialEmergencyBalance);
        return new PersonalCfoLedger(mandate, Array.AsReadOnly(months.ToArray()), Array.AsReadOnly(schedule.ToArray()));
    }

    private static void ValidateLedger(
        List<MonthlyTreasuryAllocation> months, List<ScheduledContribution> schedule, decimal initial)
    {
        var expectedOpening = initial;
        foreach (var row in months)
        {
            if (row.Essentials < 0 || row.Debt < 0 || row.Emergency < 0 || row.Investment < 0
                || row.Discretionary < 0 || row.DivertedToEmergency < 0)
                throw new InvalidOperationException("Treasury envelopes cannot be negative.");
            if (row.Salary != row.Essentials + row.Debt + row.Emergency + row.Investment + row.Discretionary)
                throw new InvalidOperationException("Monthly treasury allocation must conserve salary.");
            if (row.OpeningEmergencyBalance != expectedOpening || row.EmergencyBalance != expectedOpening + row.Emergency)
                throw new InvalidOperationException("Emergency ledger must conserve balances.");
            if (row.SafetyGate is null || (row.SafetyGate.InvestmentMultiplier == 0m && row.Investment != 0m))
                throw new InvalidOperationException("Investment must obey the CFO safety gate.");
            expectedOpening = row.EmergencyBalance;
        }
        if (schedule.Sum(x => x.Amount) != months.Sum(x => x.Investment))
            throw new InvalidOperationException("Investment schedule must equal released investment.");
    }
}
