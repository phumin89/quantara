using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class ForwardFundingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-forward-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(root, "account.json");
    private static DateTimeOffset At(string date) => DateTimeOffset.Parse(date + "T00:00:00+07:00");

    [Fact]
    public void Payday_uses_Bangkok_month_end_and_never_imports_backtest_wealth()
    {
        var store = new JsonForwardFundingStore(FilePath);
        Assert.Null(store.Read());
        var opened = store.Advance(At("2026-09-09"));
        Assert.Empty(opened.Postings);
        Assert.Equal(new DateOnly(2026, 9, 30), opened.NextPayday);
        Assert.Equal(0m, opened.InvestmentCash);
        Assert.Empty(store.Advance(At("2026-09-30").AddTicks(-1)).Postings);
        var funded = store.Advance(At("2026-09-30"));
        var row = Assert.Single(funded.Postings);
        Assert.Equal(new DateOnly(2026, 9, 30), row.Allocation.SalaryDate);
        Assert.Equal(70000m, row.Allocation.Salary);
        Assert.Equal(14000m, row.Allocation.Debt);
        Assert.Equal(24490m, funded.EmergencyBalance);
        Assert.Equal(0m, funded.InvestmentCash);
        Assert.Equal(7110m, row.Allocation.Discretionary);
        Assert.Equal("TRADING_NOT_ENABLED", funded.TradingStatus);
    }

    [Fact]
    public async Task Restart_and_concurrent_ticks_cannot_duplicate_salary()
    {
        new JsonForwardFundingStore(FilePath).Advance(At("2026-09-09"));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new JsonForwardFundingStore(FilePath).Advance(At("2026-11-30")))));
        var state = new JsonForwardFundingStore(FilePath).Read()!;
        Assert.Equal(3, state.Postings.Count);
        Assert.Equal(210000m, state.TotalSalary);
        Assert.Equal(65175m, state.EmergencyBalance);
        Assert.Equal(8295m, state.InvestmentCash);
        Assert.Equal(new DateOnly(2026, 12, 31), state.NextPayday);
        Assert.All(state.Postings, p => Assert.Equal(p.Allocation.Salary,
            p.Allocation.Essentials + p.Allocation.Debt + p.Allocation.Emergency + p.Allocation.Investment + p.Allocation.Discretionary));
        Assert.Equal(At("2026-11-30"), state.Postings[0].PostedAt);
    }

    [Fact]
    public void Calendar_crosses_years_and_leap_February_without_early_payments()
    {
        var store = new JsonForwardFundingStore(FilePath);
        store.Advance(At("2027-12-15"));
        Assert.Equal(2, store.Advance(At("2028-02-28")).Postings.Count);
        var feb = store.Advance(At("2028-02-29"));
        Assert.Equal(3, feb.Postings.Count);
        Assert.Equal(new DateOnly(2028, 2, 29), feb.Postings[^1].Allocation.SalaryDate);
        Assert.Equal(new DateOnly(2028, 3, 31), feb.NextPayday);
    }

    [Fact]
    public void Corrupt_account_never_resets_to_a_new_funded_account()
    {
        var store = new JsonForwardFundingStore(FilePath);
        store.Advance(At("2026-09-09"));
        File.WriteAllText(FilePath, "{broken");
        Assert.Throws<InvalidDataException>(() => store.Read());
        Assert.Throws<InvalidDataException>(() => store.Advance(At("2026-10-31")));
        Assert.Equal("{broken", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Deleted_account_and_backward_clock_fail_closed()
    {
        var store = new JsonForwardFundingStore(FilePath);
        store.Advance(At("2026-09-30"));
        Assert.Throws<InvalidOperationException>(() => store.Advance(At("2026-09-29")));
        File.Delete(FilePath);
        Assert.Throws<InvalidDataException>(() => store.Advance(At("2026-10-31")));
        Assert.False(File.Exists(FilePath));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
