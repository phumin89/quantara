using TradingCompany.Domain;

namespace TradingCompany.Application;

public static class ForwardFundingRules
{
    public static ForwardFundingAccount Open(DateTimeOffset now)
    {
        var first = ForwardFundingCalendar.MonthEnd(ForwardFundingCalendar.BangkokDate(now));
        return new(1, Guid.NewGuid(), now, new(first, first, 70000m, 24400m, 14000m, 115200m, .25m, .70m), []);
    }

    public static MonthlyTreasuryAllocation Allocate(PersonalCfoMandate mandate, DateOnly due, decimal reserve)
        => new PersonalCfoAllocationEngine().Run(mandate with { Start = due, End = due, InitialEmergencyBalance = reserve })
            .Months.Single() with { SalaryDate = due };

    public static void Validate(ForwardFundingAccount state)
    {
        var expected = Open(state.OpenedAt).Mandate;
        if (state.Version != 1 || state.AccountId == Guid.Empty || state.Mandate != expected || state.Postings is null)
            throw new InvalidDataException("Forward account identity or policy is invalid.");
        var due = expected.Start;
        var reserve = 0m;
        var lastRecorded = state.OpenedAt;
        foreach (var entry in state.Postings)
        {
            if (entry is null || entry.PostedAt < lastRecorded || ForwardFundingCalendar.BangkokDate(entry.PostedAt) < due
                || entry.Allocation != Allocate(expected, due, reserve))
                throw new InvalidDataException("Forward salary ledger does not reconcile.");
            reserve = entry.Allocation.EmergencyBalance;
            lastRecorded = entry.PostedAt;
            due = ForwardFundingCalendar.MonthEnd(due.AddDays(1));
        }
    }


}
