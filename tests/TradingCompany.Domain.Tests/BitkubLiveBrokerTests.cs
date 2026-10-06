using System.Net;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubLiveBrokerTests
{
    private const long Timestamp = 1699381086593;
    private const string Wallet = """
        {"code":"0","message":"success","data":[
          {"currency":"BTC","available":"1000","reserved":"100","total":"1100"},
          {"currency":"THB","available":"10","reserved":"10","total":"20"}]}
        """;
    // Official V3 order-info example: filled != total even with remaining=0.
    // Preserve the exchange evidence; do not fabricate accounting quantities from status.
    private const string OrderInfo = """
        {"error":0,"result":{"id":"289","first":"289","parent":"0","last":"316",
         "client_id":"client-1","post_only":false,"amount":"4000","rate":291000,
         "fee":10,"credit":10,"filled":3999.97,"total":4000,"status":"filled",
         "partial_filled":false,"remaining":0,"history":[
          {"amount":98.14848,"credit":0.25,"fee":0.25,"id":"289","rate":291000,
           "timestamp":1702466375000,"txn_id":"BTCBUY0003372258"}]}}
        """;

    [Fact]
    public async Task Order_info_preserves_raw_amounts_lineage_and_fill_evidence_without_claiming_reconciliation()
    {
        var handler = new FakeTransport(OrderInfo);
        using var broker = Create(handler);
        var order = await broker.GetOrderAsync("BTC-THB", "289", OrderSide.Buy);
        Assert.Equal("?sym=btc_thb&id=289&sd=buy", handler.OrderQuery);
        Assert.Equal("289", order.OrderId);
        Assert.Equal("316", order.LastOrderId);
        Assert.Equal("client-1", order.ClientId);
        Assert.Equal("filled", order.Status);
        Assert.Equal(4000m, order.RawAmount);
        Assert.Equal(3999.97m, order.RawFilled);
        Assert.Equal(0, order.RawRemaining);
        Assert.Equal(10, order.ReportedFee);
        Assert.Equal(10, order.FeeCredit);
        var fill = Assert.Single(order.History);
        Assert.Equal("BTCBUY0003372258", fill.TransactionId);
        Assert.Equal(98.14848m, fill.RawAmount);
        Assert.Equal(.25m, fill.FeeCredit);
    }

    [Theory]
    [InlineData("\"id\":\"289\",\"first\"", "\"id\":\"another-order\",\"first\"")]
    [InlineData("\"status\":\"filled\"", "\"status\":\"new-unknown-status\"")]
    [InlineData("\"remaining\":0", "\"remaining\":-1")]
    [InlineData("\"txn_id\":\"BTCBUY0003372258\"", "\"txn_id\":\"\"")]
    public async Task Invalid_order_evidence_is_unavailable_not_a_terminal_order(string original, string replacement)
    {
        using var broker = Create(new(OrderInfo.Replace(original, replacement)));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOrderAsync("BTC-THB", "289", OrderSide.Buy));
    }

    [Fact]
    public async Task Filled_manual_market_order_preserves_numeric_amount_zero_order_rate_and_actual_fill_price()
    {
        var response = OrderInfo.Replace("\"amount\":\"4000\"", "\"amount\":4000")
            .Replace("\"amount\":4000,\"rate\":291000", "\"amount\":4000,\"rate\":0");
        using var broker = Create(new(response));
        var order = await broker.GetOrderAsync("BTC-THB", "289", OrderSide.Buy);
        Assert.Equal(4000m, order.RawAmount);
        Assert.Equal(0m, order.Rate);
        Assert.Equal(291000m, Assert.Single(order.History).Rate);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("null")]
    public async Task Numeric_order_amount_never_accepts_negative_null_or_silent_decimal_rounding(string amount)
    {
        using var broker = Create(new(OrderInfo.Replace("\"amount\":\"4000\"", "\"amount\":" + amount)));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOrderAsync("BTC-THB", "289", OrderSide.Buy));
    }

    [Fact]
    public async Task Opaque_order_id_is_escaped_as_one_query_value()
    {
        var handler = new FakeTransport(OrderInfo.Replace("\"id\":\"289\",\"first\"", "\"id\":\"a&sd=sell+/=\",\"first\""));
        using var broker = Create(handler);
        await broker.GetOrderAsync("ETH-THB", "a&sd=sell+/=", OrderSide.Buy);
        Assert.Equal("?sym=eth_thb&id=a%26sd%3Dsell%2B%2F%3D&sd=buy", handler.OrderQuery);
    }

    [Fact]
    public async Task Invalid_lookup_identity_does_not_send_credentials()
    {
        var handler = new FakeTransport(OrderInfo);
        using var broker = Create(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOrderAsync("BTC-THB", "", OrderSide.Buy));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOrderAsync("BTC-THB", "289", (OrderSide)99));
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Wallet_uses_fresh_signed_v4_request_and_preserves_reserved_balance()
    {
        var handler = new FakeTransport(Wallet);
        using var broker = Create(handler);
        var balances = await broker.GetWalletAsync();
        Assert.Equal(2, balances.Count);
        var baht = Assert.Single(balances, b => b.Currency == "THB");
        Assert.Equal(10, baht.Available);
        Assert.Equal(10, baht.Reserved);
        Assert.Equal(20, baht.Total);
        Assert.Equal(new[] { "/api/v3/servertime", "/api/v4/wallet/balances" }, handler.Paths);
        Assert.Equal("1bea59517b1549dbd20183e5d3410b7e0c203d5ae238a104f38ef8d27d3fc01f", handler.Signature);
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Equal("1699381086593", handler.TimestampHeader);
        Assert.False(handler.PublicCallHadCredentials);
    }

    [Theory]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("10.00000000000000000000000000001")]
    public async Task Wallet_amounts_that_would_be_rounded_are_unavailable(string raw)
    {
        var wallet = $$$"""{"code":"0","data":[{"currency":"THB","available":"{{{raw}}}","reserved":"0","total":"{{{raw}}}"}]}""";
        using var broker = Create(new(wallet));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetWalletAsync());
    }

    [Theory]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("10.00000000000000000000000000001")]
    public async Task Fill_fees_that_would_be_rounded_are_unavailable(string raw)
    {
        using var broker = Create(new(OrderInfo.Replace("\"fee\":0.25", "\"fee\":" + raw)));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOrderAsync("BTC-THB", "289", OrderSide.Buy));
    }

    [Fact]
    public async Task Scientific_fee_underflow_is_not_treated_as_zero_fee()
    {
        using var broker = Create(new(OrderInfo.Replace("\"fee\":0.25", "\"fee\":1e-29")));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetOrderAsync("BTC-THB", "289", OrderSide.Buy));
    }

    [Theory]
    [InlineData("1e-8", "0.00000001")]
    [InlineData("0.250000000000000000000000000000", "0.25")]
    [InlineData("1e2", "100")]
    [InlineData("1e-28", "0.0000000000000000000000000001")]
    public async Task Exact_numeric_fee_formats_remain_supported(string raw, string expected)
    {
        using var broker = Create(new(OrderInfo.Replace("\"fee\":0.25", "\"fee\":" + raw)));
        var evidence = await broker.GetOrderAsync("BTC-THB", "289", OrderSide.Buy);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Assert.Single(evidence.History).ReportedFee);
    }

    [Theory]
    [InlineData("{\"code\":\"0\",\"data\":[{\"currency\":\"THB\",\"available\":\"10\",\"reserved\":\"10\",\"total\":\"30\"}]}")]
    [InlineData("{\"code\":\"0\",\"data\":[{\"currency\":\"THB\",\"available\":\"-1\",\"reserved\":\"0\",\"total\":\"-1\"}]}")]
    [InlineData("{\"code\":\"0\",\"data\":[{\"currency\":\"THB\",\"available\":\"10\",\"total\":\"10\"}]}")]
    [InlineData("{\"code\":\"0\",\"data\":[]}")]
    [InlineData("{\"code\":\"0\",\"data\":[{\"currency\":\"THB\",\"available\":\"1,000\",\"reserved\":\"0\",\"total\":\"1000\"}]}")]
    [InlineData("{\"code\":\"0\"}")]
    [InlineData("not json")]
    public async Task Malformed_balances_are_unavailable_not_zero(string response)
    {
        using var broker = Create(new(response));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetWalletAsync());
    }

    [Fact]
    public async Task Duplicate_currency_is_not_counted_twice()
    {
        using var broker = Create(new("""
            {"code":"0","data":[
              {"currency":"THB","available":"1","reserved":"0","total":"1"},
              {"currency":"THB","available":"1","reserved":"0","total":"1"}]}
            """));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetWalletAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Transport_failure_does_not_retry_or_follow_redirect(HttpStatusCode status)
    {
        var handler = new FakeTransport(Wallet) { Status = status };
        using var broker = Create(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetWalletAsync());
        Assert.Equal(2, handler.Paths.Count);
    }

    [Fact]
    public async Task Stale_server_time_prevents_sending_credentials()
    {
        var handler = new FakeTransport(Wallet) { ServerTime = Timestamp - 31000 };
        using var broker = Create(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetWalletAsync());
        Assert.Single(handler.Paths);
        Assert.Null(handler.ApiKey);
    }

    [Fact]
    public async Task Error_body_is_not_echoed_to_operator_or_logs()
    {
        using var broker = Create(new("""{"code":"AUTH_ERROR","message":"test-secret test-key"}"""));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetWalletAsync());
        Assert.DoesNotContain("test-secret", error.ToString());
        Assert.DoesNotContain("test-key", error.ToString());
    }

    [Fact]
    public async Task History_follows_cursor_and_keeps_fee_credit_separate_from_reported_fee()
    {
        var handler = new FakeTransport(Wallet) { HistoryPages = [HistoryPage("opaque+/=", true), """{"error":0,"result":[],"pagination":{"cursor":null,"has_next":false}}"""] };
        using var broker = Create(handler);
        var entries = await broker.GetHistoryAsync("BTC-THB");
        var entry = Assert.Single(entries);
        Assert.Equal("txn-opaque", entry.TransactionId);
        Assert.Equal("order-opaque", entry.OrderId);
        Assert.Equal(1000, entry.RawAmount);
        Assert.Equal(25, entry.ReportedFee);
        Assert.Equal(10, entry.FeeCredit);
        Assert.Equal("buy", entry.Side);
        Assert.Equal(2, handler.HistoryRequests.Count);
        Assert.Equal("?sym=btc_thb&pagination_type=keyset&lmt=100&cursor=opaque%2B%2F%3D", handler.HistoryRequests[1]);
    }

    [Fact]
    public async Task Repeated_cursor_does_not_report_partial_history_as_complete()
    {
        var handler = new FakeTransport(Wallet) { HistoryPages = [HistoryPage("repeat", true), HistoryPage("repeat", true)] };
        using var broker = Create(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetHistoryAsync("BTC-THB"));
        Assert.Equal(2, handler.HistoryRequests.Count);
    }

    [Theory]
    [InlineData("{\"error\":0,\"result\":[]}")]
    [InlineData("{\"error\":0,\"result\":[],\"pagination\":{\"has_next\":true,\"cursor\":null}}")]
    [InlineData("{\"error\":0,\"result\":[],\"pagination\":{\"has_next\":true,\"cursor\":\"\"}}")]
    public async Task Incomplete_pagination_blocks_reconciliation(string page)
    {
        using var broker = Create(new(Wallet) { HistoryPages = [page] });
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetHistoryAsync("BTC-THB"));
    }

    [Fact]
    public async Task Unsupported_symbol_is_rejected_before_any_network_call()
    {
        var handler = new FakeTransport(Wallet);
        using var broker = Create(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetHistoryAsync("../withdraw"));
        Assert.Empty(handler.Paths);
    }

    private static string HistoryPage(string cursor, bool hasNext) => $$$"""
        {"error":0,"result":[{
          "txn_id":"txn-opaque","order_id":"order-opaque","parent_order_id":"parent-opaque","super_order_id":"super-opaque",
          "client_id":"CLIENT123","taken_by_me":false,"is_maker":true,"side":"buy","type":"limit",
          "rate":"2500000.00","fee":"25.00","credit":"10.00","amount":"1000.00","ts":1699381086593,"order_closed_at":1699381086593}],
          "pagination":{"cursor":"{{{cursor}}}","has_next":{{{hasNext.ToString().ToLowerInvariant()}}}}}
        """;

    private static BitkubLiveBroker Create(FakeTransport handler)
        => new("test-key", "test-secret", handler, new FixedClock());

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);
    }

    private sealed class FakeTransport(string response) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public long ServerTime { get; init; } = Timestamp;
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string? Signature { get; private set; }
        public string? ApiKey { get; private set; }
        public string? TimestampHeader { get; private set; }
        public bool PublicCallHadCredentials { get; private set; }
        public string[] HistoryPages { get; init; } = [];
        public List<string> HistoryRequests { get; } = [];
        public string? OrderQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.bitkub.com", request.RequestUri.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri.AbsolutePath;
            Paths.Add(path);
            if (path == "/api/v3/servertime")
            {
                PublicCallHadCredentials = request.Headers.Contains("X-BTK-APIKEY");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ServerTime.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
            }
            if (path == "/api/v3/market/my-order-history")
            {
                Assert.True(request.Headers.Contains("X-BTK-SIGN"));
                HistoryRequests.Add(request.RequestUri.Query);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(HistoryPages[HistoryRequests.Count - 1]) });
            }
            if (path == "/api/v3/market/order-info")
            {
                Assert.True(request.Headers.Contains("X-BTK-SIGN"));
                OrderQuery = request.RequestUri.Query;
                return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(response) });
            }
            Assert.Equal("/api/v4/wallet/balances", path);
            Signature = request.Headers.GetValues("X-BTK-SIGN").Single();
            ApiKey = request.Headers.GetValues("X-BTK-APIKEY").Single();
            TimestampHeader = request.Headers.GetValues("X-BTK-TIMESTAMP").Single();
            var result = new HttpResponseMessage(Status) { Content = new StringContent(response) };
            if (Status == HttpStatusCode.Redirect) result.Headers.Location = new("https://invalid.example/steal");
            return Task.FromResult(result);
        }
    }
}
