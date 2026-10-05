using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class OwnerRememberedBrowserTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "quantara-remember-test-" + Guid.NewGuid().ToString("N"));
    private readonly X509Certificate2 certificate;

    public OwnerRememberedBrowserTests()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Quantara-test-only", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificate = request.CreateSelfSigned(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Directory.CreateDirectory(directory);
    }

    private IDataProtectionProvider Provider() => DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys")),
        builder => builder.SetApplicationName("Quantara.Owner.v1").ProtectKeysWithCertificate(certificate));

    [Fact]
    public async Task Remembered_cookie_survives_eight_hours_and_restart_but_cannot_mutate_live_state()
    {
        var clock = new Clock();
        string cookie;
        await using (var first = new Factory(clock, directory, Provider()))
        {
            using var client = first.Client();
            var response = await Pair(first, client, rememberBrowser: true);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var header = OwnerCookie(response);
            Assert.Contains("expires=", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
            cookie = header.Split(';')[0];
            clock.Now += TimeSpan.FromHours(8) + TimeSpan.FromSeconds(1);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/live/status")).StatusCode);
            await Token(client);
            await AssertControlsDenied(client);
        }
        await using var restarted = new Factory(clock, directory, Provider());
        using var restored = restarted.Client(cookie);
        var state = await restored.GetFromJsonAsync<JsonElement>("/live/status");
        Assert.Equal("DISABLED", state.GetProperty("state").GetString());
        Assert.False(state.GetProperty("armed").GetBoolean());
        // The route is authenticated, but no broker/account is registered in this test or production viewer.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await restored.GetAsync("/live/wallet")).StatusCode);
        await Token(restored);
        await AssertControlsDenied(restored);
        clock.Now += TimeSpan.FromDays(30) - TimeSpan.FromHours(8);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restored.GetAsync("/live/status")).StatusCode);
    }

    [Fact]
    public async Task Restart_immediately_revokes_control_authority_even_when_viewing_is_remembered()
    {
        var clock = new Clock();
        string cookie;
        await using (var first = new Factory(clock, directory, Provider()))
        {
            using var client = first.Client();
            cookie = OwnerCookie(await Pair(first, client, true)).Split(';')[0];
        }
        await using var restarted = new Factory(clock, directory, Provider());
        using var restored = restarted.Client(cookie);
        Assert.Equal(HttpStatusCode.OK, (await restored.GetAsync("/live/status")).StatusCode);
        await Token(restored);
        await AssertControlsDenied(restored);
    }

    [Fact]
    public async Task Logout_after_eight_hours_forgets_browser_and_revokes_copied_cookie_after_restart()
    {
        var clock = new Clock();
        string cookie;
        await using (var first = new Factory(clock, directory, Provider()))
        {
            using var client = first.Client();
            cookie = OwnerCookie(await Pair(first, client, true)).Split(';')[0];
            clock.Now += TimeSpan.FromHours(9);
            await Token(client);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/owner/logout", null)).StatusCode);
            using var copy = first.Client(cookie);
            Assert.Equal(HttpStatusCode.Unauthorized, (await copy.GetAsync("/live/status")).StatusCode);
        }
        await using var second = new Factory(clock, directory, Provider());
        using var restored = second.Client(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restored.GetAsync("/live/status")).StatusCode);
    }

    [Fact]
    public async Task Opting_out_keeps_session_cookie_and_does_not_restore_it_after_restart()
    {
        var clock = new Clock();
        string cookie;
        await using (var first = new Factory(clock, directory, Provider()))
        {
            using var client = first.Client();
            var header = OwnerCookie(await Pair(first, client, false));
            Assert.DoesNotContain("expires=", header, StringComparison.OrdinalIgnoreCase);
            cookie = header.Split(';')[0];
        }
        await using var second = new Factory(clock, directory, Provider());
        using var restored = second.Client(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restored.GetAsync("/live/status")).StatusCode);
    }

    [Fact]
    public void Persisted_state_contains_no_session_token_and_corruption_fails_closed()
    {
        var clock = new Clock();
        var sessions = new OwnerSession(clock, Provider(), directory);
        var paired = sessions.TryPair(sessions.IssuePairingCode(), true);
        Assert.Equal(204, paired.Status);
        var file = Path.Combine(directory, "session.bin");
        Assert.DoesNotContain(paired.SessionId!, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        var keyFile = Assert.Single(Directory.GetFiles(Path.Combine(directory, "keys"), "key-*.xml"));
        Assert.Contains("encryptedSecret", File.ReadAllText(keyFile));
        File.WriteAllText(file, "corrupt-test-state");
        Assert.False(new OwnerSession(clock, Provider(), directory).IsActive(paired.SessionId));
    }

    [Fact]
    public void Replacing_pairing_code_revokes_remembered_session_on_disk_as_well_as_in_memory()
    {
        var clock = new Clock();
        var sessions = new OwnerSession(clock, Provider(), directory);
        var paired = sessions.TryPair(sessions.IssuePairingCode(), true);
        sessions.IssuePairingCode();
        Assert.False(sessions.IsActive(paired.SessionId));
        Assert.False(new OwnerSession(clock, Provider(), directory).IsActive(paired.SessionId));
    }

    private static async Task AssertControlsDenied(HttpClient client)
    {
        foreach (var path in new[] { "/live/preflight", "/live/arm", "/live/mandate", "/live/stop", "/live/baseline",
            "/live/orders/33333333-3333-3333-3333-333333333333/cancel" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { })).StatusCode);
    }

    private static string OwnerCookie(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-QuantaraOwner="));

    private static async Task<HttpResponseMessage> Pair(Factory factory, HttpClient client, bool rememberBrowser)
    {
        var code = factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode();
        await Token(client);
        return await client.PostAsJsonAsync("/owner/pair", new { code, rememberBrowser });
    }

    private static async Task Token(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/owner/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Factory(Clock clock, string directory, IDataProtectionProvider provider) : WebApplicationFactory<LiveProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton(provider);
                services.RemoveAll<OwnerSession>();
                services.AddSingleton(new OwnerSession(clock, provider, directory));
            });
        }

        public HttpClient Client(string? cookie = null)
        {
            var client = CreateClient(new() { BaseAddress = new("https://localhost:7443"), AllowAutoRedirect = false, HandleCookies = cookie is null });
            client.DefaultRequestHeaders.Add("Origin", "https://localhost:3000");
            if (cookie is not null) client.DefaultRequestHeaders.Add("Cookie", cookie);
            return client;
        }
    }

    public void Dispose()
    {
        certificate.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
