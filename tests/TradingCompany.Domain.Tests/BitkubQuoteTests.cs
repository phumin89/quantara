using System.Net;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubQuoteTests
{
    [Fact]
    public async Task Reads_public_quote_without_credentials_and_uses_request_time()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 5, 1, 2, 3, TimeSpan.Zero));
        var wire = new Wire("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"3380000\",\"lowest_ask\":\"3390000\"}]", clock);
        using var broker = new BitkubLiveBroker("test-key", "test-secret", wire, clock);
        var quote = await broker.GetQuoteAsync("BTC-THB");
        Assert.Equal(new LiveQuote("BTC-THB", 3380000m, 3390000m, clock.GetUtcNow()), quote);
        Assert.Equal("/api/v3/market/ticker?sym=btc_thb", wire.Path);
        Assert.False(wire.Credentials);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"symbol\":\"ETH_THB\",\"highest_bid\":\"1\",\"lowest_ask\":\"2\"}]")]
    [InlineData("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"1\",\"lowest_ask\":\"2\"},{\"symbol\":\"BTC_THB\",\"highest_bid\":\"1\",\"lowest_ask\":\"2\"}]")]
    [InlineData("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"0\",\"lowest_ask\":\"2\"}]")]
    [InlineData("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"2\",\"lowest_ask\":\"1\"}]")]
    [InlineData("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"999999999999999999999999999999999999999999\",\"lowest_ask\":\"2\"}]")]
    [InlineData("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"1\",\"highest_bid\":\"1\",\"lowest_ask\":\"2\"}]")]
    public async Task Rejects_unusable_public_quote(string payload)
    {
        using var broker = new BitkubLiveBroker("test-key", "test-secret", new Wire(payload, new Clock(DateTimeOffset.UtcNow)), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetQuoteAsync("BTC-THB"));
    }

    [Fact]
    public async Task Observation_is_taken_before_a_slow_read()
    {
        var clock = new AdvancingClock(new DateTimeOffset(2026, 10, 5, 1, 2, 3, TimeSpan.Zero));
        var wire = new Wire("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"1\",\"lowest_ask\":\"2\"}]", clock, () => clock.Advance());
        using var broker = new BitkubLiveBroker("test-key", "test-secret", wire, clock);
        var quote = await broker.GetQuoteAsync("BTC-THB");
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 1, 2, 3, TimeSpan.Zero), quote.ObservedAt);
    }

    [Fact]
    public async Task Malformed_symbol_fails_before_network()
    {
        var wire = new Wire("[]", new Clock(DateTimeOffset.UtcNow));
        using var broker = new BitkubLiveBroker("test-key", "test-secret", wire, wire.Clock);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetQuoteAsync("DOGE-THB?injected=1"));
        Assert.Null(wire.Path);
    }

    private class Clock(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class AdvancingClock(DateTimeOffset value) : Clock(value)
    {
        private DateTimeOffset current = value;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance() => current = current.AddMinutes(1);
    }
    private sealed class Wire(string payload, Clock clock, Action? onRead = null) : HttpMessageHandler
    {
        public Clock Clock { get; } = clock;
        public string? Path { get; private set; }
        public bool Credentials { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Path = request.RequestUri!.PathAndQuery;
            Credentials = request.Headers.Any(h => h.Key.StartsWith("X-BTK-", StringComparison.OrdinalIgnoreCase));
            onRead?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }
}
