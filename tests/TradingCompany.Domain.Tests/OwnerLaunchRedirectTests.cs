using Microsoft.AspNetCore.Http;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class OwnerLaunchRedirectTests
{
    [Theory]
    [InlineData("localhost", null)]
    [InlineData("localhost", 2080)]
    [InlineData("127.0.0.1", null)]
    [InlineData("127.0.0.1", 2080)]
    public void Plain_local_navigation_redirects_to_frontend_without_query(string host, int? port)
    {
        foreach (var method in new[] { "GET", "HEAD" })
        foreach (var path in new[] { "/", "/account", "/trading" })
        {
            var context = Context(method, host, port, path + "?next=https://evil.example/steal");
            OwnerLaunchRedirect.Handle(context);
            Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
            Assert.Equal("https://localhost:3000" + path, context.Response.Headers.Location.ToString());
            Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        }
    }

    [Theory]
    [InlineData("https", "localhost", 443, "GET", "/")]
    [InlineData("http", "example.com", 2080, "GET", "/")]
    [InlineData("http", "localhost", 3000, "GET", "/")]
    [InlineData("http", "localhost", 2080, "POST", "/")]
    [InlineData("http", "localhost", 2080, "OPTIONS", "/")]
    [InlineData("http", "localhost", 2080, "GET", "/live/arm")]
    [InlineData("http", "localhost", 2080, "GET", "/owner/pair")]
    [InlineData("http", "localhost", 2080, "GET", "/internal/live")]
    public void Non_launch_requests_are_forbidden_without_location(string scheme, string host, int port, string method, string path)
    {
        var context = Context(method, host, port, path);
        context.Request.Scheme = scheme;
        OwnerLaunchRedirect.Handle(context);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    private static DefaultHttpContext Context(string method, string host, int? port, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Method = method;
        context.Request.Host = port is { } value ? new HostString(host, value) : new HostString(host);
        context.Request.Path = path.Split('?')[0];
        context.Request.QueryString = new QueryString(path.Contains('?') ? "?" + path.Split('?')[1] : "");
        context.Response.Body = new MemoryStream();
        return context;
    }
}
