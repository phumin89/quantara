using System.Net;
using TradingCompany.Api;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubForwardPaperQuotesTests
{
    private static readonly DateTimeOffset Received = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Returns_valid_depth_quote_with_http_observation_and_request_controls()
    {
        HttpRequestMessage? seen = null;
        var adapter = Create(new DelegateHandler((request, _) => { seen = request; return Task.FromResult(Response("{\"error\":0,\"result\":{\"asks\":[[101,2]],\"bids\":[[99,3]]}}")); }));
        var result = await adapter.GetAsync("BTC-THB", Received, default);
        Assert.NotNull(result);
        Assert.Equal(100m, result.NativePrice); Assert.Equal(1m, result.SlippageThbPerUnit);
        Assert.Equal(.0025m, result.FeeRate); Assert.Equal(2m, result.MaximumQuantity);
        Assert.Equal(Received.AddSeconds(-5), result.ObservedAt); Assert.Equal(Received, result.AvailableAt);
        Assert.Equal("https://api.bitkub.com/api/v3/market/depth?sym=BTC_THB&lmt=1", seen!.RequestUri!.ToString());
        Assert.True(seen.Headers.CacheControl!.NoCache);
        Assert.DoesNotContain(seen.Headers, h => h.Key.Contains("AUTH", StringComparison.OrdinalIgnoreCase) ||
            h.Key.Contains("KEY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Supports_only_known_markets_without_http_for_unknown()
    {
        var handler = new CountingHandler(); var adapter = Create(handler);
        Assert.Equal(new[] { "BTC-THB", "ETH-THB", "SOL-THB" }, adapter.SupportedSymbols);
        Assert.Null(await adapter.GetAsync("DOGE-THB", Received, default)); Assert.Equal(0, handler.Count);
    }

    [Theory]
    [InlineData("{\"error\":1,\"result\":{\"asks\":[[101,2]],\"bids\":[[99,3]]}}")]
    [InlineData("{\"error\":0,\"result\":{\"asks\":[],\"bids\":[[99,3]]}}")]
    [InlineData("{\"error\":0,\"result\":{\"asks\":[[101,0]],\"bids\":[[99,3]]}}")]
    [InlineData("{\"error\":0,\"result\":{\"asks\":[[103,2]],\"bids\":[[99,3]]}}")]
    [InlineData("{\"error\":0,\"result\":{\"asks\":[[-101,2]],\"bids\":[[99,3]]}}")]
    [InlineData("{\"error\":true,\"result\":{\"asks\":[[101,2]],\"bids\":[[99,3]]}}")]
    [InlineData("{\"error\":0,\"result\":{\"asks\":[[true,2]],\"bids\":[[99,3]]}}")]
    [InlineData("{\"error\":0,\"result\":{\"asks\":[[101,2]],\"bids\":[[102,3]]}}")]
    public async Task Rejects_invalid_depth(string body)
    { Assert.Null(await Create(new DelegateHandler((_, _) => Task.FromResult(Response(body)))).GetAsync("BTC-THB", Received, default)); }

    [Fact]
    public async Task Rejects_missing_stale_future_and_nonzero_age_headers()
    {
        foreach (var setup in new Func<HttpResponseMessage>[] { () => ResponseWithoutDate(), () => ResponseAt(Received.AddSeconds(-31)), () => ResponseAt(Received.AddSeconds(1)), () => ResponseWithAge() })
            Assert.Null(await Create(new DelegateHandler((_, _) => Task.FromResult(setup()))).GetAsync("BTC-THB", Received, default));
    }

    [Fact]
    public async Task Rejects_http_failure_and_slow_or_rolled_back_clock()
    {
        Assert.Null(await Create(new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)))).GetAsync("BTC-THB", Received, default));
        Assert.Null(await Create(new DelegateHandler((_, _) => Task.FromResult(ResponseAt(Received)))).GetAsync("BTC-THB", Received.AddSeconds(-21), default));
        Assert.Null(await Create(new DelegateHandler((_, _) => Task.FromResult(ResponseAt(Received.AddSeconds(-5))))).GetAsync("BTC-THB", Received.AddSeconds(1), default));
    }

    [Fact]
    public async Task Returns_null_for_transient_timeout_and_propagates_caller_cancellation()
    {
        var timeout = new DelegateHandler((_, _) => throw new OperationCanceledException());
        Assert.Null(await Create(timeout).GetAsync("BTC-THB", Received, default));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(timeout).GetAsync("BTC-THB", Received, cts.Token));
    }

    [Fact]
    public async Task Maps_each_supported_symbol_to_get_endpoint()
    {
        foreach (var symbol in new[] { "BTC-THB", "ETH-THB", "SOL-THB" })
        {
            HttpRequestMessage? seen = null;
            await Create(new DelegateHandler((request, _) => { seen = request; return Task.FromResult(Response("{\"error\":0,\"result\":{\"asks\":[[101,2]],\"bids\":[[99,3]]}}")); })).GetAsync(symbol, Received, default);
            Assert.Equal(HttpMethod.Get, seen!.Method);
            Assert.Contains(symbol[..3] + "_THB", seen.RequestUri!.Query);
        }
    }

    private static BitkubForwardPaperQuotes Create(HttpMessageHandler handler) => new(new FakeFactory(handler), new FixedTimeProvider(Received));
    private static HttpResponseMessage Response(string body) { var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }; r.Headers.Date = Received.AddSeconds(-5); return r; }
    private static HttpResponseMessage ResponseAt(DateTimeOffset date) { var r = Response("{\"error\":0,\"result\":{\"asks\":[[101,2]],\"bids\":[[99,3]]}}"); r.Headers.Date = date; return r; }
    private static HttpResponseMessage ResponseWithoutDate() => new(HttpStatusCode.OK) { Content = new StringContent("{\"error\":0,\"result\":{\"asks\":[[101,2]],\"bids\":[[99,3]]}}") };
    private static HttpResponseMessage ResponseWithAge() { var r = ResponseAt(Received.AddSeconds(-5)); r.Headers.Age = TimeSpan.FromSeconds(1); return r; }
    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class FakeFactory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h); }
    private sealed class CountingHandler : HttpMessageHandler { public int Count; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) { Count++; return Task.FromResult(Response("{}")); } }
    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> f) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => f(r, c); }
}
