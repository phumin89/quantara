using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveOwnerAccessTests
{
    private const string Origin = "https://localhost:3000";

    [Theory]
    [InlineData("/live/status")]
    [InlineData("/live/account")]
    [InlineData("/live/wallet")]
    [InlineData("/live/activity")]
    public async Task Anonymous_cannot_read_live_state(string path)
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Owner_can_read_empty_activity_but_cannot_publish_advisory_events()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.NoContent, (await Pair(client, factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode())).StatusCode);
        var result = await client.GetFromJsonAsync<JsonElement>("/live/activity");
        Assert.Equal(JsonValueKind.Null, result.GetProperty("activity").ValueKind);
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/internal/live/activity", new { })).StatusCode);
    }

    [Fact]
    public async Task Pairing_is_one_use_and_never_arms_or_fabricates_an_account()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var code = factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode();
        Assert.Equal(32, Convert.FromHexString(code).Length);
        var paired = await Pair(client, code);
        Assert.Equal(HttpStatusCode.NoContent, paired.StatusCode);
        var cookie = Assert.Single(paired.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-QuantaraOwner="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var status = await client.GetFromJsonAsync<JsonElement>("/live/status");
        Assert.Equal("DISABLED", status.GetProperty("state").GetString());
        Assert.False(status.GetProperty("armed").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/live/account")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/live/wallet")).StatusCode);
        using var other = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pair(other, code)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/live/status")).StatusCode);
    }

    [Fact]
    public async Task Expired_pairing_code_cannot_start_a_session()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var code = factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode();
        factory.Clock.Now += TimeSpan.FromMinutes(5);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pair(client, code)).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_codes_are_rate_limited_without_consuming_valid_code()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var code = factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode();
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Pair(client, new string('0', 64))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Pair(client, code)).StatusCode);
        factory.Clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(HttpStatusCode.NoContent, (await Pair(client, code)).StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:7443", Origin)]
    [InlineData("https://attacker.invalid:7443", Origin)]
    [InlineData("https://localhost:7444", Origin)]
    [InlineData("https://localhost:7443", "https://attacker.invalid")]
    [InlineData("https://localhost:7443", "null")]
    public async Task Wrong_transport_host_or_origin_is_rejected(string address, string origin)
    {
        await using var factory = new Factory();
        using var client = factory.Client(address);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/owner/csrf")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pairing_requires_antiforgery_and_explicit_origin(bool tokenWithoutOrigin)
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var code = factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode();
        if (tokenWithoutOrigin)
        {
            await SetToken(client);
            client.DefaultRequestHeaders.Remove("Origin");
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/owner/pair", new { code })).StatusCode);
        using var validClient = factory.Client();
        Assert.Equal(HttpStatusCode.NoContent, (await Pair(validClient, code)).StatusCode);
    }

    [Fact]
    public async Task Session_expires_after_eight_hours_without_sliding_renewal()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.NoContent,
            (await Pair(client, factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode())).StatusCode);
        factory.Clock.Now += TimeSpan.FromHours(7);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/live/status")).StatusCode);
        factory.Clock.Now += TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/live/status")).StatusCode);
    }

    [Fact]
    public async Task Logout_requires_csrf_and_revokes_even_a_copied_cookie()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var response = await Pair(client, factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode());
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), s => s.StartsWith("__Host-QuantaraOwner=")).Split(';')[0];
        using var copy = factory.Client(handleCookies: false);
        copy.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await copy.GetAsync("/live/status")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/owner/logout", null)).StatusCode);
        await SetToken(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/owner/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copy.GetAsync("/live/status")).StatusCode);
    }

    [Fact]
    public async Task Restart_does_not_accept_the_previous_owner_cookie()
    {
        await using var first = new Factory();
        using var client = first.Client();
        var response = await Pair(client, first.Services.GetRequiredService<OwnerSession>().IssuePairingCode());
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), s => s.StartsWith("__Host-QuantaraOwner=")).Split(';')[0];
        await using var second = new Factory();
        using var restarted = second.Client(handleCookies: false);
        restarted.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restarted.GetAsync("/live/status")).StatusCode);
    }

    private static async Task<HttpResponseMessage> Pair(HttpClient client, string code)
    {
        await SetToken(client);
        return await client.PostAsJsonAsync("/owner/pair", new { code });
    }

    private static async Task SetToken(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/owner/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Factory : WebApplicationFactory<LiveProgram>
    {
        public TestClock Clock { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }
        public HttpClient Client(string address = "https://localhost:7443", bool handleCookies = true)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri(address), AllowAutoRedirect = false, HandleCookies = handleCookies
            });
            client.DefaultRequestHeaders.Add("Origin", Origin);
            return client;
        }
    }
}
