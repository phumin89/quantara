using System.Net;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubPermissionTests
{
    [Theory]
    [InlineData("{\"error\":0,\"result\":\"synthetic-discarded-token-1234567890\"}", true)]
    [InlineData("{\"error\":52}", false)]
    [InlineData("{\"error\":52,\"result\":null}", false)]
    public async Task Signed_token_probe_checks_permission_without_financial_endpoints(string response, bool expected)
    {
        var handler = new Handler(response);
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, new Clock());
        Assert.Equal(expected, await broker.CheckTradePermissionAsync());
        Assert.Equal(1, handler.Probes);
    }

    [Theory]
    [InlineData("{\"error\":0,\"result\":\"short\"}")]
    [InlineData("{\"error\":0,\"result\":null}")]
    [InlineData("{\"error\":0,\"result\":17}")]
    [InlineData("{\"error\":0,\"result\":\"synthetic-token-with-spaces 1234567890\"}")]
    [InlineData("{\"error\":0,\"error\":52,\"result\":\"synthetic-discarded-token-1234567890\"}")]
    [InlineData("{\"error\":52,\"result\":\"synthetic-discarded-token-1234567890\"}")]
    [InlineData("{\"error\":90}")]
    [InlineData("{\"error\":3}")]
    public async Task Unknown_or_malformed_permission_evidence_is_not_a_grant_or_denial(string response)
    {
        var handler = new Handler(response);
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, new Clock());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => broker.CheckTradePermissionAsync());
        Assert.DoesNotContain("test-secret", error.ToString());
        Assert.DoesNotContain("synthetic-discarded-token", error.ToString());
        Assert.Equal(1, handler.Probes);
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("network")]
    [InlineData("server")]
    public async Task Unavailable_probe_is_sanitized_and_never_retried(string failure)
    {
        var handler = new Handler("test-secret") { Failure = failure };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, new Clock());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => broker.CheckTradePermissionAsync());
        Assert.DoesNotContain("test-secret", error.ToString());
        Assert.Equal(1, handler.Probes);
    }

    private sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(1791262800000); }
    private sealed class Handler(string response) : HttpMessageHandler
    {
        public int Probes { get; private set; }
        public string? Failure { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://api.bitkub.com", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            if (request.RequestUri.AbsolutePath == "/api/v3/servertime")
                return new(HttpStatusCode.OK) { Content = new StringContent("1791262800000") };
            Assert.Equal("/api/v3/market/wstoken", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("{}", await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
            Assert.Equal("test-key", request.Headers.GetValues("X-BTK-APIKEY").Single());
            Assert.Single(request.Headers.GetValues("X-BTK-SIGN"));
            Probes++;
            if (Failure == "network") throw new HttpRequestException("test-secret");
            return new(Failure == "redirect" ? HttpStatusCode.Redirect : Failure == "server" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            { Content = new StringContent(response) };
        }
    }
}
