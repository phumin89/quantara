using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record ForwardSalaryPosting(DateTimeOffset PostedAt, MonthlyTreasuryAllocation Allocation);

public sealed record ForwardFundingAccount(int Version, Guid AccountId, DateTimeOffset OpenedAt,
    PersonalCfoMandate Mandate, IReadOnlyList<ForwardSalaryPosting> Postings)
{
    public string TradingStatus => "TRADING_NOT_ENABLED";
    public decimal TotalSalary => Postings.Sum(p => p.Allocation.Salary);
    public decimal EmergencyBalance => Postings.LastOrDefault()?.Allocation.EmergencyBalance ?? 0m;
    public decimal InvestmentCash => Postings.Sum(p => p.Allocation.Investment);
    public DateOnly NextPayday => ForwardFundingCalendar.MonthEnd(Postings.Count == 0
        ? Mandate.Start : Postings[^1].Allocation.SalaryDate.AddDays(1));
}

public interface IForwardFundingStore
{
    ForwardFundingAccount? Read();
    ForwardFundingAccount Advance(DateTimeOffset now);
}

public static class ForwardFundingCalendar
{
    public static DateOnly BangkokDate(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
    public static DateOnly MonthEnd(DateOnly date) => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
