using System.Net;
using Microsoft.AspNetCore.Http;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class OwnerWebProxyTests
{
    [Fact]
    public async Task Frontend_receives_only_public_get_without_owner_cookies_or_authorization()
    {
        var handler=new Handler(); using var client=new HttpClient(handler);
        var context=Context("https","localhost:3000","GET");
        context.Request.Headers.Cookie="__Host-QuantaraOwner=private-cookie";
        context.Request.Headers.Authorization="Bearer private-token";
        context.Request.Path="/_next/static/app.js";
        await OwnerWebProxy.ForwardAsync(context,client);
        var expectedFrontend = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ? "http://owner-web:3001" : "http://127.0.0.1:3001";
        Assert.Equal(expectedFrontend+"/_next/static/app.js",handler.Uri);
        Assert.Empty(handler.Headers);
        Assert.Equal(200,context.Response.StatusCode);
    }
    [Theory]
    [InlineData("http","localhost:3000","GET",403)]
    [InlineData("https","attacker.invalid:3000","GET",403)]
    [InlineData("https","localhost:3000","POST",405)]
    public async Task Wrong_transport_host_and_mutations_never_reach_frontend(string scheme,string host,string method,int expected)
    {
        var handler=new Handler();using var client=new HttpClient(handler);
        var context=Context(scheme,host,method);
        await OwnerWebProxy.ForwardAsync(context,client);
        Assert.Equal(expected,context.Response.StatusCode); Assert.Null(handler.Uri);
    }
    [Fact]
    public async Task Client_navigation_reaches_next_without_owner_credentials_or_arbitrary_headers()
    {
        var handler = new Handler(); using var client = new HttpClient(handler);
        var context = Context("https", "localhost:3000", "GET");
        context.Request.Path = "/account";
        context.Request.QueryString = new("?_rsc=test");
        context.Request.Headers["RSC"] = "1";
        context.Request.Headers["Next-Router-State-Tree"] = "%5B%22%22%5D";
        context.Request.Headers.Cookie = "__Host-QuantaraOwner=private-cookie";
        context.Request.Headers.Authorization = "Bearer private-token";
        context.Request.Headers["X-CSRF-TOKEN"] = "private-csrf";
        context.Request.Headers["X-Arbitrary"] = "private-value";
        await OwnerWebProxy.ForwardAsync(context, client);
        Assert.EndsWith("/account?_rsc=test", handler.Uri);
        Assert.Equal(["Next-Router-State-Tree", "RSC"], handler.Headers.Order().ToArray());
        Assert.Equal("1", handler.Values["RSC"]);
        Assert.Equal("%5B%22%22%5D", handler.Values["Next-Router-State-Tree"]);
        var cache = System.Net.Http.Headers.CacheControlHeaderValue.Parse(context.Response.Headers.CacheControl.ToString());
        Assert.True(cache.Private); Assert.True(cache.NoStore);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }
    [Fact]
    public async Task Oversized_navigation_header_never_reaches_frontend()
    {
        var handler = new Handler(); using var client = new HttpClient(handler);
        var context = Context("https", "localhost:3000", "GET");
        context.Request.Headers["Next-Router-State-Tree"] = new string('x', 8193);
        await OwnerWebProxy.ForwardAsync(context, client);
        Assert.Equal(400, context.Response.StatusCode); Assert.Null(handler.Uri);
    }
    [Theory]
    [InlineData("/account?_rsc=corrected", true)]
    [InlineData("https://attacker.invalid/account", false)]
    [InlineData("//attacker.invalid/account", false)]
    public async Task Navigation_redirects_stay_on_the_public_owner_origin(string location, bool allowed)
    {
        using var client = new HttpClient(new RedirectHandler(location));
        var context = Context("https", "localhost:3000", "GET");
        await OwnerWebProxy.ForwardAsync(context, client);
        Assert.Equal(307, context.Response.StatusCode);
        Assert.Equal(allowed ? location : "", context.Response.Headers.Location.ToString());
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }
    private static DefaultHttpContext Context(string scheme,string host,string method)
    {var context=new DefaultHttpContext();context.Request.Scheme=scheme;context.Request.Host=new(host);context.Request.Method=method;context.Response.Body=new MemoryStream();return context;}
    private sealed class Handler : HttpMessageHandler
    {
        public string? Uri;public string[] Headers=[];
        public Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Uri=request.RequestUri!.ToString();Headers=request.Headers.Select(h=>h.Key).ToArray();
            Values = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var response = new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("public")};
            response.Headers.TryAddWithoutValidation("Cache-Control", "private, no-store");
            response.Headers.TryAddWithoutValidation("Set-Cookie", "private-cookie=do-not-forward");
            return Task.FromResult(response);
        }
    }
    private sealed class RedirectHandler(string location) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Content = new StringContent("") };
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            response.Headers.TryAddWithoutValidation("Set-Cookie", "do-not-forward=private");
            return Task.FromResult(response);
        }
    }
}
