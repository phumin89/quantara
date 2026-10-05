using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class SandboxControlApiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-control-api-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TradingAgents:Company:Enabled"] = "false", ["ForwardFunding:Enabled"] = "false",
            ["ForwardFunding:StatePath"] = Path.Combine(root, "funding.json")
        }));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LoopbackTestFilter>());
    });

    [Fact]
    public async Task Unauthorized_control_requests_cannot_change_state()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/sandbox/control")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/sandbox/control", new { paused = true })).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/sandbox/control", new { paused = true })).StatusCode);
        Assert.False(File.Exists(Path.Combine(root, "funding.json.control.json")));
    }

    [Fact]
    public async Task Device_control_is_confirmed_in_status_and_persisted_without_opening_accounts()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");
        var nonce = await client.GetFromJsonAsync<JsonElement>("/sandbox/control");
        client.DefaultRequestHeaders.Add("X-Sandbox-Control", nonce.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/sandbox/control", new { })).StatusCode);
        var pause = await client.PostAsJsonAsync("/sandbox/control", new { paused = true });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        var state = await client.GetFromJsonAsync<JsonElement>("/forward-company/status");
        Assert.True(state.GetProperty("paused").GetBoolean());
        Assert.Equal("PAUSED", state.GetProperty("status").GetString());
        Assert.True(File.Exists(Path.Combine(root, "funding.json.control.json")));
        Assert.False(File.Exists(Path.Combine(root, "funding.json")));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/sandbox/control", new { paused = false })).StatusCode);
        state = await client.GetFromJsonAsync<JsonElement>("/forward-company/status");
        Assert.False(state.GetProperty("paused").GetBoolean());
    }

    private sealed class LoopbackTestFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; await nextMiddleware(); });
            next(app);
        };
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
