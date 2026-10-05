using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingCompany.Api;
using TradingCompany.Application;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CompanyStartupDefaultsTests
{
    [Fact]
    public void Codex_sandbox_uses_cli_gateway_instead_of_metered_api_gateway()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardFunding:Enabled"] = "false",
                ["ForwardFunding:StatePath"] = Path.Combine(Path.GetTempPath(), $"quantara-default-{Guid.NewGuid():N}.json"),
                ["TradingAgents:Company:Gateway"] = "codex-cli",
                ["TradingAgents:Company:Enabled"] = "false"
            }));
        });

        _ = factory.CreateClient();
        var gateway = factory.Services.GetRequiredService<ITradingAgentGateway>();
        Assert.Equal("CodexCliTradingAgentGateway", gateway.GetType().Name);
    }

    [Fact]
    public void Unknown_agent_gateway_cannot_silently_fall_back_to_metered_api()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardFunding:Enabled"] = "false",
                ["ForwardFunding:StatePath"] = Path.Combine(Path.GetTempPath(), $"quantara-unknown-{Guid.NewGuid():N}.json"),
                ["TradingAgents:Company:Gateway"] = "codex-typo"
            }));
        });

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Unknown trading-agent gateway", error.Message);
    }

    [Fact]
    public async Task Default_company_is_sandbox_with_model_disabled_and_legacy_routes_removed()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardFunding:StatePath"] = Path.Combine(Path.GetTempPath(), $"quantara-default-{Guid.NewGuid():N}.json"),
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITradingAgentGateway>();
                services.AddSingleton<ITradingAgentGateway, ThrowingGateway>();
            });
        });

        using var client = factory.CreateClient();
        Assert.False(factory.Services.GetRequiredService<IAiCompanyMeetingRunner>().Enabled);

        using var statusResponse = await client.GetAsync("/forward-company/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal("forward-paper", status.RootElement.GetProperty("mode").GetString());
        Assert.False(status.RootElement.GetProperty("modelEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/portfolio")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/company/status")).StatusCode);
    }

    private sealed class ThrowingGateway : ITradingAgentGateway
    {
        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The disabled AI company must not invoke the gateway.");
    }
}
