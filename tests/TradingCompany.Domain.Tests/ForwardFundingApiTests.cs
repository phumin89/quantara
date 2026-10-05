using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class ForwardFundingApiTests : IDisposable
{
    private readonly FixedClock clock = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-forward-api-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> Factory(bool enabled, decimal initialCapital = 0m) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TradingAgents:Company:Enabled"] = "false", ["ForwardFunding:Enabled"] = enabled.ToString(),
            ["ForwardFunding:StatePath"] = Path.Combine(root, "forward.json"),
            ["ForwardFunding:InitialPaperCapital"] = initialCapital.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }));
        b.ConfigureServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock); });
    });

    [Fact]
    public async Task Configured_sandbox_capital_is_visible_without_changing_salary_or_legacy_demo()
    {
        await using var factory = Factory(true, 100000m);
        using var client = factory.CreateClient();
        var demo = SeedLegacyArchive();
        factory.Services.GetRequiredService<TradingCompany.Api.ForwardFundingService>().Tick();
        factory.Services.GetRequiredService<TradingCompany.Api.ForwardFundingService>().Tick();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/forward-funding/status"));
        Assert.Equal("PORTFOLIO_FUNDED", json.RootElement.GetProperty("status").GetString());
        var portfolio = json.RootElement.GetProperty("portfolio");
        Assert.Equal(100000m, portfolio.GetProperty("cash").GetDecimal());
        Assert.Equal(100000m, portfolio.GetProperty("equity").GetDecimal());
        Assert.Equal(100000m, portfolio.GetProperty("initialCapital").GetDecimal());
        Assert.Equal(0m, portfolio.GetProperty("totalPnl").GetDecimal());
        Assert.Empty(portfolio.GetProperty("credits").EnumerateArray());
        Assert.Equal(0m, json.RootElement.GetProperty("account").GetProperty("totalSalary").GetDecimal());
        Assert.Equal(0m, json.RootElement.GetProperty("account").GetProperty("emergencyBalance").GetDecimal());
        Assert.Equal(demo, File.ReadAllText(Path.Combine(root, "shared.json")));
    }

    [Fact]
    public async Task Disabled_account_is_not_created_by_status_reads()
    {
        await using var factory = Factory(false);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/forward-funding/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DISABLED", json.RootElement.GetProperty("status").GetString());
        Assert.False(File.Exists(Path.Combine(root, "forward.json")));
    }

    [Fact]
    public async Task Forward_company_status_reports_model_disabled_without_paid_calls()
    {
        await using var factory = Factory(true);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/forward-company/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("modelEnabled").GetBoolean());
        Assert.Equal("forward-paper", json.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Enabled_worker_opens_zero_account_without_changing_shared_portfolio()
    {
        await using var factory = Factory(true);
        using var client = factory.CreateClient();
        var before = SeedLegacyArchive();
        // Explicitly exercise the same worker tick with the controlled clock, not a timing race.
        factory.Services.GetRequiredService<TradingCompany.Api.ForwardFundingService>().Tick();
        using var response = await client.GetAsync("/forward-funding/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var account = json.RootElement.GetProperty("account");
        Assert.Equal("2026-09-30", account.GetProperty("nextPayday").GetString());
        Assert.Equal(0m, account.GetProperty("totalSalary").GetDecimal());
        Assert.Equal("TRADING_NOT_ENABLED", account.GetProperty("tradingStatus").GetString());
        Assert.Equal(before, File.ReadAllText(Path.Combine(root, "shared.json")));
    }

    [Fact]
    public async Task Monthly_investment_reaches_isolated_portfolio_once_and_status_reconciles()
    {
        await using var factory = Factory(true);
        using var client = factory.CreateClient();
        var service = factory.Services.GetRequiredService<TradingCompany.Api.ForwardFundingService>();
        service.Tick();
        var demoBefore = SeedLegacyArchive();
        clock.Now = DateTimeOffset.Parse("2026-11-30T00:00:00+07:00");
        service.Tick(); service.Tick();
        using var response = await client.GetAsync("/forward-funding/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement;
        Assert.Equal("PORTFOLIO_FUNDED", result.GetProperty("status").GetString());
        Assert.Equal(0m, result.GetProperty("pendingInvestment").GetDecimal());
        var portfolio = result.GetProperty("portfolio");
        Assert.Equal(2, portfolio.GetProperty("version").GetInt32());
        Assert.Empty(portfolio.GetProperty("pendingOrders").EnumerateArray());
        Assert.Equal(8295m, portfolio.GetProperty("cash").GetDecimal());
        Assert.Equal(0m, portfolio.GetProperty("totalPnl").GetDecimal());
        Assert.Equal(1, portfolio.GetProperty("credits").GetArrayLength());
        Assert.Equal(65175m, result.GetProperty("account").GetProperty("emergencyBalance").GetDecimal());
        Assert.Equal(demoBefore, File.ReadAllText(Path.Combine(root, "shared.json")));
    }

    private string SeedLegacyArchive()
    {
        Directory.CreateDirectory(root);
        const string saved = "legacy paper archive retained unchanged";
        File.WriteAllText(Path.Combine(root, "shared.json"), saved);
        return saved;
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-09T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
