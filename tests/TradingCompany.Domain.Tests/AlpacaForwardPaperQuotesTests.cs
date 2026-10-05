using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using TradingCompany.Api;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class AlpacaForwardPaperQuotesTests
{
    [Fact]
    public async Task Returns_mid_price_and_converted_spread_with_scoped_auth()
    {
        var now = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
        HttpRequestMessage? seen = null;
        var adapter = Create(new DelegateHandler((request, _) => { seen = request; return Task.FromResult(Response("{\"symbol\":\"NVDA\",\"quote\":{\"ap\":100.1,\"bp\":99.9,\"t\":\"2026-09-12T09:55:00Z\"}}")); }),
            new([new(new(2026, 9, 12), 36m)]));
        var result = await adapter.GetAsync("NVDA", now, default);
        Assert.NotNull(result);
        Assert.Equal(100m, result.NativePrice);
        Assert.Equal(36m, result.ThbPerUnit);
        Assert.Equal(3.6m, result.SlippageThbPerUnit);
        Assert.Equal(now, result.AvailableAt);
        Assert.Equal("alpaca-iex-paper-spread", result.Source);
        Assert.Equal("https://data.alpaca.markets/v2/stocks/NVDA/quotes/latest?feed=iex", seen!.RequestUri!.ToString());
        Assert.Equal("key", seen.Headers.GetValues("APCA-API-KEY-ID").Single());
    }

    [Fact]
    public async Task Rejects_unsupported_missing_credentials_and_future_quotes_without_http()
    {
        var handler = new CountingHandler();
        var noCredentials = new AlpacaForwardPaperQuotes(new FakeFactory(handler), new FakeRates(), new ConfigurationBuilder().Build());
        Assert.Null(await noCredentials.GetAsync("NVDA", DateTimeOffset.UtcNow, default));
        Assert.Null(await noCredentials.GetAsync("TSLA", DateTimeOffset.UtcNow, default));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Rejects_wrong_json_and_stale_fx()
    {
        var now = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
        var wrong = Create(new DelegateHandler((_, _) => Task.FromResult(Response("{\"symbol\":\"NVDA\",\"quote\":{\"ap\":true,\"bp\":99.9,\"t\":\"2026-09-12T09:55:00Z\"}}"))),
            new([new(new(2026, 9, 12), 36m)]));
        Assert.Null(await wrong.GetAsync("NVDA", now, default));
        var stale = Create(new DelegateHandler((_, _) => Task.FromResult(Response("{\"symbol\":\"NVDA\",\"quote\":{\"ap\":100.1,\"bp\":99.9,\"t\":\"2026-09-12T09:55:00Z\"}}"))),
            new([new(new(2026, 9, 1), 36m)]));
        Assert.Null(await stale.GetAsync("NVDA", now, default));
    }

    private static AlpacaForwardPaperQuotes Create(HttpMessageHandler handler, FakeRates rates)
        => new(new FakeFactory(handler), rates, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["APCA_API_KEY_ID"] = "key", ["APCA_API_SECRET_KEY"] = "secret" }).Build());
    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler); }
    private sealed class FakeRates(IReadOnlyList<HistoricalUsdThbRate>? rows = null) : IUsdThbRateProvider
    {
        public Task<decimal> GetAsync(CancellationToken cancellationToken) => Task.FromResult(36m);
        public Task<IReadOnlyList<HistoricalUsdThbRate>> GetHistoricalAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
            => Task.FromResult(rows ?? (IReadOnlyList<HistoricalUsdThbRate>)[]);
    }
    private sealed class CountingHandler : HttpMessageHandler
    { public int Count { get; private set; } protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Count++; return Task.FromResult(Response("{}")); } }
    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken); }
}
