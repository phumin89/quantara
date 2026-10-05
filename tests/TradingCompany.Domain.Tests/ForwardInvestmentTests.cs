using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class ForwardInvestmentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-investment-" + Guid.NewGuid().ToString("N"));
    private string PortfolioPath => Path.Combine(root, "portfolio.json");
    private static DateTimeOffset At(string date) => DateTimeOffset.Parse(date + "T00:00:00+07:00");
    private JsonForwardInvestmentStore Store() => new(PortfolioPath);
    private ForwardFundingAccount Funding()
    {
        var store = new JsonForwardFundingStore(Path.Combine(root, "funding.json"));
        store.Advance(At("2026-09-09"));
        return store.Advance(At("2026-11-30"));
    }

    [Fact]
    public void Only_investment_allocation_becomes_portfolio_cash_not_income_or_reserves()
    {
        var funding = Funding();
        var account = Store().Synchronize(funding, At("2026-12-01"));
        Assert.Equal(8295m, account.Cash);
        Assert.Equal(8295m, account.TotalContributed);
        Assert.Equal(0m, account.TotalPnl);
        Assert.Equal(funding.AccountId, account.FundingAccountId);
        Assert.Equal(65175m, funding.EmergencyBalance);
        Assert.Equal(210000m, funding.TotalSalary);
        Assert.Equal(At("2026-12-01"), Assert.Single(account.Credits).CreditedAt);
        Assert.Equal(new DateOnly(2026, 11, 30), account.Credits[0].SalaryDate);
    }

    [Fact]
    public async Task Restart_and_concurrent_syncs_credit_once()
    {
        var funding = Funding();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Store().Synchronize(funding, At("2026-12-01")))));
        Assert.Single(Store().Read()!.Credits);
        Assert.Equal(8295m, Store().Read()!.Cash);
        Assert.Equal(Store().Read()!.Credits, Store().Synchronize(funding, At("2026-12-02")).Credits);
    }

    [Fact]
    public void Empty_funding_never_imports_demo_capital()
    {
        var funding = ForwardFundingRules.Open(At("2026-09-09"));
        Assert.Null(Store().Read());
        var account = Store().Synchronize(funding, At("2026-09-09"));
        Assert.Empty(account.Credits); Assert.Equal(0m, account.Equity);
    }

    [Fact]
    public void Other_account_and_clock_rollback_are_rejected()
    {
        var funding = Funding(); Store().Synchronize(funding, At("2026-12-01"));
        Assert.Throws<InvalidDataException>(() => Store().Synchronize(funding with { AccountId = Guid.NewGuid() }, At("2026-12-02")));
        Assert.Throws<InvalidOperationException>(() => Store().Synchronize(funding, At("2026-11-29")));
    }

    [Fact]
    public void Treasury_rollback_cannot_erase_existing_credit()
    {
        var funding = Funding(); Store().Synchronize(funding, At("2026-12-01"));
        Assert.Throws<InvalidDataException>(() => Store().Synchronize(funding with { Postings = funding.Postings.Take(2).ToArray() }, At("2026-12-02")));
        Assert.Equal(8295m, Store().Read()!.Cash);
    }

    [Fact]
    public void Corrupt_or_missing_initialized_portfolio_is_not_recreated()
    {
        var funding = Funding(); Store().Synchronize(funding, At("2026-12-01"));
        using (var stream = new FileStream(PortfolioPath, FileMode.Open, FileAccess.Write)) stream.SetLength(0);
        Assert.Throws<InvalidDataException>(() => Store().Synchronize(funding, At("2026-12-02")));
        File.Delete(PortfolioPath);
        Assert.Throws<InvalidDataException>(() => Store().Synchronize(funding, At("2026-12-02")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
