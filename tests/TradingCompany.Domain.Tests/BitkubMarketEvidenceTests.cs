using System.Net;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubMarketEvidenceTests
{
    // Adapted from public symbols captured 2026-09-29; sell freeze enabled to test preservation.
    private const string SymbolItem = """
        {"base_asset":"BTC","base_asset_scale":8,"base_quantity_scale":8,"buy_price_gap_as_percent":20,
        "created_at":"2017-10-30T22:16:10+07:00","description":"Thai Baht to Bitcoin",
        "freeze_buy":false,"freeze_cancel":false,"freeze_sell":true,"market_segment":"SPOT",
        "min_quote_size":10,"modified_at":"2026-09-16T02:00:33.411+07:00","name":"Bitcoin","pairing_id":1,
        "price_scale":2,"price_step":"0.01","quantity_scale":0,"quantity_step":"1","quote_asset":"THB",
        "quote_asset_scale":2,"quote_quantity_scale":2,"sell_price_gap_as_percent":20,"status":"active",
        "symbol":"BTC_THB","source":"exchange"}
        """;
    private const string OpenItem = """
        {"id":"opaque-order","side":"buy","type":"limit","rate":"10","fee":"0.25","credit":"0",
        "amount":"100","receive":"9.975","parent_id":"0","super_id":"0","client_id":"","ts":1707220636000}
        """;
    private static string Envelope(string item) => "{\"error\":0,\"result\":[" + item + "]}";

    [Fact]
    public async Task Market_rules_preserve_distinct_scales_and_freezes_without_private_headers()
    {
        var wire = new Wire(Envelope(SymbolItem));
        using var broker = Create(wire);
        var result = await broker.GetSymbolRulesAsync("BTC-THB");
        Assert.Equal(new BrokerSymbolRules("BTC-THB", new Clock().GetUtcNow(), 8, 2, 8, 2, 2, .01m, 0,
            1, 10, false, true, false, "active", "exchange", "SPOT"), result);
        Assert.Equal(new[] { "/api/v3/market/symbols" }, wire.Paths);
        Assert.False(wire.PublicCredentials);
    }

    [Fact]
    public async Task Missing_quantity_scales_are_not_guessed_from_asset_scales()
    {
        using var broker = Create(new(Envelope(SymbolItem.Replace("\"base_quantity_scale\":8,", "").Replace("\"quote_quantity_scale\":2,", ""))));
        var rules = await broker.GetSymbolRulesAsync("BTC-THB");
        Assert.Null(rules.BaseQuantityScale);
        Assert.Null(rules.QuoteQuantityScale);
    }

    [Theory]
    [InlineData("\"base_asset\":\"BTC\"", "\"base_asset\":\"ETH\"")]
    [InlineData("\"price_step\":\"0.01\"", "\"price_step\":\"0\"")]
    [InlineData("\"quantity_scale\":0", "\"quantity_scale\":29")]
    [InlineData("\"base_quantity_scale\":8", "\"base_quantity_scale\":8,\"base_quantity_scale\":2")]
    [InlineData("\"freeze_buy\":false", "\"freeze_buy\":null")]
    [InlineData("\"min_quote_size\":10", "\"min_quote_size\":-1")]
    public async Task Malformed_market_rules_never_become_usable_defaults(string from, string to)
    {
        using var broker = Create(new(Envelope(SymbolItem.Replace(from, to))));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetSymbolRulesAsync("BTC-THB"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_duplicate_market_symbol_is_unavailable(bool duplicate)
    {
        using var broker = Create(new(Envelope(duplicate ? SymbolItem + "," + SymbolItem : "")));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetSymbolRulesAsync("BTC-THB"));
    }

    [Fact]
    public async Task Open_orders_preserve_side_units_manual_orders_and_fee_credits()
    {
        var sell = OpenItem.Replace("opaque-order", "sell-order").Replace("\"buy\"", "\"sell\"")
            .Replace("\"amount\":\"100\"", "\"amount\":\"0.5\"").Replace("\"receive\":\"9.975\"", "\"receive\":\"5\"")
            .Replace("\"credit\":\"0\"", "\"credit\":\"0.25\"");
        var wire = new Wire(Envelope(OpenItem + "," + sell));
        using var broker = Create(wire);
        var result = await broker.GetOpenOrdersAsync("BTC-THB");
        Assert.Equal(2, result.Count);
        Assert.Equal(new BrokerOpenOrderEvidence("BTC-THB", "opaque-order", OrderSide.Buy, "limit", 10, 100,
            9.975m, .25m, 0, "0", "0", "", 1707220636000), result[0]);
        Assert.Equal(OrderSide.Sell, result[1].Side);
        Assert.Equal(.5m, result[1].RawAmount);
        Assert.Equal(5m, result[1].RawReceive);
        Assert.Equal(.25m, result[1].ReportedFee);
        Assert.Equal(.25m, result[1].FeeCredit);
        Assert.Equal(new[] { "/api/v3/servertime", "/api/v3/market/my-open-orders?sym=btc_thb" }, wire.Paths);
        Assert.False(wire.PublicCredentials);
    }

    [Theory]
    [InlineData("\"side\":\"buy\"", "\"side\":\"other\"")]
    [InlineData("\"id\":\"opaque-order\"", "\"id\":\"\"")]
    [InlineData("\"amount\":\"100\"", "\"amount\":\"-1\"")]
    [InlineData("\"receive\":\"9.975\"", "\"receive\":null")]
    [InlineData("\"client_id\":\"\"", "\"client_id\":null")]
    [InlineData("\"ts\":1707220636000", "\"ts\":0")]
    public async Task Malformed_open_order_is_not_silently_dropped(string from, string to)
    {
        using var broker = Create(new(Envelope(OpenItem.Replace(from, to))));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOpenOrdersAsync("BTC-THB"));
    }

    [Fact]
    public async Task Duplicate_open_order_identity_is_rejected_not_double_counted()
    {
        using var broker = Create(new(Envelope(OpenItem + "," + OpenItem)));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOpenOrdersAsync("BTC-THB"));
    }

    [Fact]
    public async Task Empty_open_order_list_does_not_invent_an_order()
    {
        using var broker = Create(new(Envelope("")));
        Assert.Empty(await broker.GetOpenOrdersAsync("BTC-THB"));
    }

    [Fact]
    public async Task Invalid_symbol_does_not_contact_public_or_private_endpoint()
    {
        var wire = new Wire("{}");
        using var broker = Create(wire);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetSymbolRulesAsync("../withdraw"));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOpenOrdersAsync("../withdraw"));
        Assert.Empty(wire.Paths);
    }

    private static BitkubLiveBroker Create(Wire wire) => new("test-key", "test-secret", wire, new Clock());
    private sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(1699381086593); }
    private sealed class Wire(string payload) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public bool PublicCredentials { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.bitkub.com", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            var path = request.RequestUri.PathAndQuery;
            Paths.Add(path);
            if (path == "/api/v3/servertime" || path == "/api/v3/market/symbols")
                PublicCredentials |= request.Headers.Any(h => h.Key.StartsWith("X-BTK-", StringComparison.OrdinalIgnoreCase));
            else
            {
                Assert.Equal("/api/v3/market/my-open-orders?sym=btc_thb", path);
                Assert.Equal("test-key", request.Headers.GetValues("X-BTK-APIKEY").Single());
                Assert.True(request.Headers.Contains("X-BTK-SIGN"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(path == "/api/v3/servertime" ? "1699381086593" : payload) });
        }
    }
}
