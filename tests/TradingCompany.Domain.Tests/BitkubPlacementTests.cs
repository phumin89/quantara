using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubPlacementTests
{
    private static readonly Guid IntentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string BuyBody = "{\"sym\":\"btc_thb\",\"amt\":1000,\"rat\":2500000,\"typ\":\"limit\",\"client_id\":\"11111111222233334444555555555555\"}";
    private const string SellBody = "{\"sym\":\"btc_thb\",\"amt\":0.0004,\"rat\":2500000,\"typ\":\"limit\",\"client_id\":\"11111111222233334444555555555555\"}";
    private static LiveIntent Intent(OrderSide side = OrderSide.Buy) => LiveRiskTests.Intent with
        { Id = IntentId, Side = side, Quantity = .00040000m, LimitPrice = 2500000.00m };
    private static PreparedLiveOrder Packet(OrderSide side = OrderSide.Buy)
    {
        var body = side == OrderSide.Buy ? BuyBody : SellBody;
        return new(Intent(side), body, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))));
    }

    [Fact]
    public async Task Fee_inclusive_budget_is_not_silently_encoded_as_broker_spending_amount()
    {
        var wire = new Transport(Receipt());
        using var broker = Create(wire);
        var packet = Packet() with { Intent = Intent() with { BuyBudget = 1000m } };
        var result = await broker.SendLimitRequestAsync(packet);
        Assert.Equal(BrokerSubmissionState.Rejected, result.State);
        Assert.Equal("INVALID_PREPARED_REQUEST", result.EvidenceCode);
        Assert.Equal(0, wire.Calls);
        Assert.Equal(0, wire.Placements);
    }
    private static string Receipt(OrderSide side = OrderSide.Buy) =>
        "{\"error\":0,\"result\":{\"id\":\"opaque/order/1\",\"typ\":\"limit\",\"amt\":" +
        (side == OrderSide.Buy ? "1000" : "0.0004") +
        ",\"rat\":2500000,\"fee\":2.5,\"cre\":1,\"rec\":0.0003994,\"ts\":\"1699381086\",\"ci\":\"11111111222233334444555555555555\"}}";

    [Theory]
    [InlineData(OrderSide.Buy)]
    [InlineData(OrderSide.Sell)]
    public void Wire_encoding_preserves_side_units_exact_decimals_and_approved_intent(OrderSide side)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var packet = BitkubLiveBroker.EncodeLimitRequest(Intent(side));
            Assert.Equal(side == OrderSide.Buy ? BuyBody : SellBody, packet.Body);
            Assert.Equal(Packet(side).BodyHash, packet.BodyHash);
            Assert.Equal(Intent(side), packet.Intent);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(OrderSide.Buy, "/api/v3/market/place-bid", "547103b802ce81a38a551a384370bf2fe2083b5017377a08979587d7ce655f15")]
    [InlineData(OrderSide.Sell, "/api/v3/market/place-ask", "536ac1ec12091150b66200ae3c999276887aff7b1f1b07a2386e193326272058")]
    public async Task Exact_prepared_body_is_sent_once_and_acknowledgement_is_not_a_fill(OrderSide side, string endpoint, string signature)
    {
        var wire = new Transport(Receipt(side));
        using var broker = Create(wire);
        var result = await broker.SendLimitRequestAsync(Packet(side));
        Assert.Equal(BrokerSubmissionState.Accepted, result.State);
        Assert.Equal("opaque/order/1", result.BrokerId);
        Assert.Equal(endpoint, wire.Endpoint);
        Assert.Equal(side == OrderSide.Buy ? BuyBody : SellBody, wire.Body);
        Assert.Equal(1, wire.Placements);
        Assert.Equal(2, wire.Calls);
        Assert.Equal(signature, wire.Signature);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("hash")]
    [InlineData("intent")]
    [InlineData("paper")]
    [InlineData("symbol")]
    [InlineData("side")]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("overflow")]
    [InlineData("account")]
    [InlineData("decision")]
    [InlineData("id")]
    [InlineData("mandate")]
    [InlineData("snapshot")]
    [InlineData("evidence")]
    public async Task Tampered_or_paper_packet_is_rejected_before_any_network_access(string change)
    {
        var packet = Packet();
        packet = change switch
        {
            "body" => packet with { Body = BuyBody.Replace("1000", "10000") },
            "hash" => packet with { BodyHash = "not-a-hash" },
            "intent" => packet with { Intent = packet.Intent with { Quantity = .4m } },
            "paper" => packet with { Intent = packet.Intent with { Mode = TradingMode.Sandbox } },
            "side" => packet with { Intent = packet.Intent with { Side = (OrderSide)99 } },
            "quantity" => packet with { Intent = packet.Intent with { Quantity = 0 } },
            "price" => packet with { Intent = packet.Intent with { LimitPrice = -1 } },
            "overflow" => packet with { Intent = packet.Intent with { Quantity = decimal.MaxValue } },
            "account" => packet with { Intent = packet.Intent with { AccountId = new(Guid.Empty) } },
            "decision" => packet with { Intent = packet.Intent with { DecisionId = Guid.Empty } },
            "id" => packet with { Intent = packet.Intent with { Id = Guid.Empty } },
            "mandate" => packet with { Intent = packet.Intent with { MandateVersion = 0 } },
            "snapshot" => packet with { Intent = packet.Intent with { SnapshotVersion = 0 } },
            "evidence" => packet with { Intent = packet.Intent with { EvidenceHash = "" } },
            _ => packet with { Intent = packet.Intent with { Symbol = "../withdraw" } }
        };
        var wire = new Transport(Receipt());
        using var broker = Create(wire);
        var result = await broker.SendLimitRequestAsync(packet);
        Assert.Equal(BrokerSubmissionState.Rejected, result.State);
        Assert.Null(result.BrokerId);
        Assert.Equal(0, wire.Calls);
    }

    [Theory]
    [InlineData(18, BrokerSubmissionState.Rejected)]
    [InlineData(52, BrokerSubmissionState.Rejected)]
    [InlineData(55, BrokerSubmissionState.Rejected)]
    [InlineData(19, BrokerSubmissionState.Unknown)]
    [InlineData(20, BrokerSubmissionState.Unknown)]
    [InlineData(90, BrokerSubmissionState.Unknown)]
    [InlineData(999, BrokerSubmissionState.Unknown)]
    public async Task Only_unambiguous_exchange_rejections_release_the_request(int code, BrokerSubmissionState state)
    {
        var wire = new Transport("{\"error\":" + code + "}");
        using var broker = Create(wire);
        var result = await broker.SendLimitRequestAsync(Packet());
        Assert.Equal(state, result.State);
        Assert.Null(result.BrokerId);
        Assert.Equal(1, wire.Placements);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("client")]
    [InlineData("amount")]
    [InlineData("duplicate")]
    [InlineData("mixed")]
    [InlineData("price")]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("fee")]
    [InlineData("credit")]
    [InlineData("receive")]
    [InlineData("timestamp")]
    public async Task Incomplete_or_contradictory_receipt_is_unknown_not_rejected_or_filled(string change)
    {
        var reply = change switch
        {
            "empty" => "{\"error\":0,\"result\":{}}",
            "client" => Receipt().Replace("11111111222233334444555555555555", "other-client"),
            "amount" => Receipt().Replace("\"amt\":1000", "\"amt\":2000"),
            "duplicate" => Receipt().Replace("\"error\":0", "\"error\":0,\"error\":18"),
            "price" => Receipt().Replace("\"rat\":2500000", "\"rat\":1"),
            "id" => Receipt().Replace("opaque/order/1", ""),
            "type" => Receipt().Replace("limit", "market"),
            "fee" => Receipt().Replace("\"fee\":2.5", "\"fee\":-2.5"),
            "credit" => Receipt().Replace("\"cre\":1", "\"cre\":-1"),
            "receive" => Receipt().Replace("\"rec\":0.0003994", "\"rec\":null"),
            "timestamp" => Receipt().Replace("1699381086", "invalid"),
            _ => Receipt().Replace("\"error\":0", "\"error\":18")
        };
        var wire = new Transport(reply);
        using var broker = Create(wire);
        Assert.Equal(BrokerSubmissionState.Unknown, (await broker.SendLimitRequestAsync(Packet())).State);
        Assert.Equal(1, wire.Placements);
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("server")]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("throttle")]
    public async Task Ambiguous_transport_outcome_never_retries_or_echoes_provider_details(string failure)
    {
        var wire = new Transport("test-secret") { Failure = failure };
        using var broker = Create(wire);
        var result = await broker.SendLimitRequestAsync(Packet());
        Assert.Equal(BrokerSubmissionState.Unknown, result.State);
        Assert.Equal(1, wire.Placements);
        Assert.Equal(2, wire.Calls);
        Assert.DoesNotContain("test-secret", result.ToString());
    }

    private static BitkubLiveBroker Create(Transport wire) => new("test-key", "test-secret", wire, new Clock());
    private sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(1699381086593); }
    private sealed class Transport(string reply) : HttpMessageHandler
    {
        public string? Failure { get; init; }
        public int Calls { get; private set; }
        public int Placements { get; private set; }
        public string? Endpoint { get; private set; }
        public string? Body { get; private set; }
        public string? Signature { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.Equal("https://api.bitkub.com", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            if (request.RequestUri.AbsolutePath == "/api/v3/servertime")
            {
                Assert.False(request.Headers.Contains("X-BTK-APIKEY"));
                return new(HttpStatusCode.OK) { Content = new StringContent("1699381086593") };
            }
            Placements++;
            Endpoint = request.RequestUri.AbsolutePath;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("test-key", request.Headers.GetValues("X-BTK-APIKEY").Single());
            Body = await request.Content.ReadAsStringAsync(token);
            Signature = request.Headers.GetValues("X-BTK-SIGN").Single();
            if (Failure == "timeout") throw new TaskCanceledException("test-secret");
            if (Failure == "network") throw new HttpRequestException("test-secret");
            var status = Failure switch
            {
                "redirect" => HttpStatusCode.Redirect,
                "server" => HttpStatusCode.InternalServerError,
                "throttle" => HttpStatusCode.TooManyRequests,
                _ => HttpStatusCode.OK
            };
            var response = new HttpResponseMessage(status) { Content = new StringContent(reply) };
            if (Failure == "redirect") response.Headers.Location = new("https://invalid.example/steal");
            return response;
        }
    }
}
