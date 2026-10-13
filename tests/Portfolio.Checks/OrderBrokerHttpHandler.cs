using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Portfolio.Checks;

public sealed class OrderBrokerHttpHandler : HttpMessageHandler
{
    public List<string> Paths { get; } = [];

    public List<string> Bodies { get; } = [];

    public bool FailPost { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var path = request.RequestUri?.PathAndQuery ?? throw new InvalidOperationException("No path");
        this.Paths.Add(path);
        if (request.Method == HttpMethod.Post)
        {
            var body = await (request.Content ?? throw new InvalidOperationException("No body")).ReadAsStringAsync(
                cancellationToken
            );
            this.Bodies.Add(body);
            var expected = Convert
                .ToHexString(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes("fake-secret"),
                        Encoding.UTF8.GetBytes("1701251212273POST" + path + body)
                    )
                )
                .ToLowerInvariant();
            if (
                request.Headers.GetValues("X-BTK-SIGN").Single() != expected
                || request.Headers.GetValues("X-BTK-APIKEY").Single() != "fake-key"
            )
            {
                throw new InvalidOperationException("Wrong POST signature");
            }

            if (this.FailPost)
            {
                throw new HttpRequestException("Fixture POST failure");
            }

            return OrderBrokerHttpHandler.Json("{\"error\":0,\"result\":{\"id\":\"order-123\"}}");
        }

        if (path == "/api/v3/servertime")
        {
            return OrderBrokerHttpHandler.Json("1701251212273");
        }

        if (path.StartsWith("/api/v3/market/ticker", StringComparison.Ordinal))
        {
            return OrderBrokerHttpHandler.Json("[{\"symbol\":\"BTC_THB\",\"last\":\"100\"}]");
        }

        if (path == "/api/v3/market/symbols")
        {
            return OrderBrokerHttpHandler.Json(
                "{\"error\":0,\"result\":[{\"symbol\":\"BTC_THB\",\"status\":\"active\",\"market_segment\":\"SPOT\",\"source\":\"exchange\",\"freeze_buy\":false,\"freeze_sell\":false,\"price_scale\":2,\"quote_asset_scale\":2,\"base_asset_scale\":8,\"price_step\":\"0.01\",\"min_quote_size\":10}]}"
            );
        }

        throw new InvalidOperationException("Unexpected endpoint; no broker network is allowed in these checks.");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
