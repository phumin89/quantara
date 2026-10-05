using System.Net;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubCancellationTests
{
    [Theory]
    [InlineData(OrderSide.Buy, "buy", "6783e2305a76f786fac7c6cd63d460589e795325286ca56c10106a94db7df65c")]
    [InlineData(OrderSide.Sell, "sell", "4f83b1cc9e81a21bf9dc45027023a6ea07a20994323436049a805c04e630eec0")]
    public async Task Cancellation_signs_exact_saved_identity_once(OrderSide side, string wireSide, string signature)
    {
        var transport = new Transport("{\"error\":0}");
        using var broker = new BitkubLiveBroker("test-key", "test-secret", transport, new Clock());
        var result = await broker.CancelAsync("BTC-THB", "opaque-1", side, default);
        Assert.Equal(BrokerSubmissionState.Accepted, result.State);
        Assert.Equal("{\"sym\":\"btc_thb\",\"id\":\"opaque-1\",\"sd\":\"" + wireSide + "\"}", transport.Body);
        Assert.Equal(1, transport.CancelCalls);
        Assert.Equal(signature, transport.Signature);
    }

    [Theory]
    [InlineData("{\"error\":21}", BrokerSubmissionState.Rejected)]
    [InlineData("{\"error\":23}", BrokerSubmissionState.Unknown)]
    [InlineData("{\"error\":90}", BrokerSubmissionState.Unknown)]
    [InlineData("{\"error\":9001}", BrokerSubmissionState.Unknown)]
    [InlineData("{\"error\":0,\"error\":21}", BrokerSubmissionState.Unknown)]
    [InlineData("{\"message\":\"test-secret\"}", BrokerSubmissionState.Unknown)]
    public async Task Cancellation_reply_cannot_be_assumed_successful(string body, BrokerSubmissionState expected)
    {
        var transport = new Transport(body);
        using var broker = new BitkubLiveBroker("test-key", "test-secret", transport, new Clock());
        var result = await broker.CancelAsync("BTC-THB", "opaque-1", OrderSide.Buy, default);
        Assert.Equal(expected, result.State);
        Assert.Equal(1, transport.CancelCalls);
        Assert.DoesNotContain("test-secret", result.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Transport_failure_is_unknown_without_retry_or_redirect(HttpStatusCode status)
    {
        var transport = new Transport("{\"error\":0}") { Status = status };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", transport, new Clock());
        Assert.Equal(BrokerSubmissionState.Unknown, (await broker.CancelAsync("BTC-THB", "opaque-1", OrderSide.Buy, default)).State);
        Assert.Equal(1, transport.CancelCalls);
    }

    [Fact]
    public async Task Invalid_identity_does_not_make_any_request()
    {
        var transport = new Transport("{\"error\":0}");
        using var broker = new BitkubLiveBroker("test-key", "test-secret", transport, new Clock());
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.CancelAsync("../withdraw", "opaque-1", OrderSide.Buy, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.CancelAsync("BTC-THB", "", OrderSide.Buy, default));
        Assert.Equal(0, transport.TotalCalls);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(1699381086593);
    }

    private sealed class Transport(string response) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public int TotalCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public string? Body { get; private set; }
        public string? Signature { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TotalCalls++;
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.bitkub.com", request.RequestUri.Host);
            if (request.RequestUri.AbsolutePath == "/api/v3/servertime")
            {
                Assert.False(request.Headers.Contains("X-BTK-APIKEY"));
                return new(HttpStatusCode.OK) { Content = new StringContent("1699381086593") };
            }
            Assert.Equal("/api/v3/market/cancel-order", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("test-key", request.Headers.GetValues("X-BTK-APIKEY").Single());
            Assert.Equal("1699381086593", request.Headers.GetValues("X-BTK-TIMESTAMP").Single());
            CancelCalls++;
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
            Signature = request.Headers.GetValues("X-BTK-SIGN").Single();
            var result = new HttpResponseMessage(Status) { Content = new StringContent(response) };
            if (Status == HttpStatusCode.Redirect) result.Headers.Location = new("https://invalid.example/steal");
            return result;
        }
    }
}
