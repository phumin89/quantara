using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingCompany.Api;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveCompanyHostTests
{
    [Fact]
    public void Disabled_advisory_host_registers_no_work_or_credentials()
    {
        var services = new ServiceCollection();
        LiveCompanyHost.Configure(services, Config([]));
        Assert.Empty(services);
    }

    [Fact]
    public void Enabled_host_registers_only_advisory_services_without_broker_ledger_or_sandbox_scheduler()
    {
        var services = new ServiceCollection();
        LiveCompanyHost.Configure(services, Config([]) );
        Assert.Empty(services);
        LiveCompanyHost.Configure(services, Config([
            new("LiveCompany:Enabled", "true"), new("LiveCompany:AccountId", LiveRiskTests.Account.Value.ToString()),
            new("LiveCompany:CoordinatorToken", new string('t', 64)), new("LiveCompany:DataPath", Path.GetTempPath()),
            new("LiveCompany:Model", "codex-default") ]));
        Assert.Contains(services, s => s.ServiceType == typeof(LiveCompanyCoordinator));
        Assert.Contains(services, s => s.ServiceType == typeof(ITradingAgentGateway));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(ILiveBroker) || s.ServiceType == typeof(ILiveTradingStore) ||
            s.ServiceType == typeof(BitkubLiveBroker) || s.ImplementationType == typeof(ForwardPaperCompanyService));
    }

    [Theory]
    [InlineData("Bitkub:CredentialsFile")]
    [InlineData("ConnectionStrings:LiveTrading")]
    public void Advisory_host_rejects_private_financial_capabilities(string key)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidDataException>(() => LiveCompanyHost.Configure(services, Config([
            new("LiveCompany:Enabled", "true"), new("LiveCompany:AccountId", LiveRiskTests.Account.Value.ToString()), new(key, "private")])));
        Assert.Empty(services);
    }
    private static IConfiguration Config(KeyValuePair<string, string?>[] settings) => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
