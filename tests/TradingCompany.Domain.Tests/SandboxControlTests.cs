using System.Net;
using Microsoft.AspNetCore.Http;
using TradingCompany.Api;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class SandboxControlTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-control-" + Guid.NewGuid().ToString("N"));
    private SandboxControl Control => new(Path.Combine(root, "control.json"), "http://localhost:3000");
    private static DefaultHttpContext Request(string origin = "http://localhost:3000", string host = "localhost:8080", string address = "127.0.0.1")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        context.Request.Host = new HostString(host);
        context.Request.Headers.Origin = origin;
        return context;
    }

    [Fact]
    public void Paused_state_survives_reconstruction_and_explicit_resume()
    {
        var control = Control;
        Assert.False(control.Paused);
        control.SetPaused(true);
        Assert.True(Control.Paused);
        Control.SetPaused(false);
        Assert.False(control.Paused);
    }

    [Fact]
    public void Missing_or_corrupt_saved_state_cannot_be_reset_by_resume()
    {
        Control.SetPaused(true);
        File.Delete(Path.Combine(root, "control.json"));
        Assert.Throws<InvalidDataException>(() => Control.SetPaused(false));
        File.WriteAllText(Path.Combine(root, "control.json"), "{}");
        Assert.Throws<InvalidDataException>(() => Control.SetPaused(false));
    }

    [Fact]
    public void Control_requires_loopback_exact_origin_host_and_process_token()
    {
        var control = Control;
        Assert.True(control.Trusted(Request()));
        Assert.False(control.Trusted(Request(origin: "https://evil.example")));
        Assert.False(control.Trusted(Request(origin: "")));
        Assert.False(control.Trusted(Request(host: "rebind.example:8080")));
        Assert.False(control.Trusted(Request(address: "192.0.2.1")));
        var request = Request();
        Assert.False(control.Trusted(request, true));
        request.Request.Headers["X-Sandbox-Control"] = control.Token;
        Assert.True(control.Trusted(request, true));
        Assert.False(Control.Trusted(request, true)); // Restart revokes the capability.
    }

    [Fact]
    public void Docker_host_proxy_requires_exact_address_origin_host_and_process_token()
    {
        var control = new SandboxControl(Path.Combine(root, "control.json"), "https://localhost:3000", IPAddress.Parse("172.24.0.1"));
        var request = Request(origin: "https://localhost:3000", address: "::ffff:172.24.0.1");
        Assert.True(control.Trusted(request));
        Assert.False(control.Trusted(request, true));
        request.Request.Headers["X-Sandbox-Control"] = control.Token;
        Assert.True(control.Trusted(request, true));
        Assert.False(control.Trusted(Request(origin: "https://evil.example", address: "172.24.0.1")));
        Assert.False(control.Trusted(Request(origin: "https://localhost:3000", host: "api:8080", address: "172.24.0.1")));
        Assert.False(control.Trusted(Request(origin: "https://localhost:3000", address: "172.24.0.2")));
        Assert.False(Control.Trusted(Request(address: "172.24.0.1")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
