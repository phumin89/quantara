using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Checks;

public sealed class TradingBrokerHttpHandler : HttpMessageHandler
{
    public string Json { get; set; } = "{}";

    public Func<string, string>? Response { get; set; }

    public List<string> GetPaths { get; } = [];

    public int Posts { get; private set; }

    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing fixture URI.");
        if (uri.Host != "bitkub.invalid")
        {
            throw new InvalidOperationException("Fixture must never access a real broker.");
        }

        if (request.Method == HttpMethod.Get)
        {
            this.GetPaths.Add(uri.PathAndQuery);
        }

        if (uri.AbsolutePath.EndsWith("servertime", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("1792100000000") };
        }

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var timestamp = request.Headers.GetValues("X-BTK-TIMESTAMP").Single();
        var expected = Convert
            .ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes("fake-secret"),
                    Encoding.UTF8.GetBytes(timestamp + request.Method.Method + uri.PathAndQuery + body)
                )
            )
            .ToLowerInvariant();
        if (
            request.Headers.GetValues("X-BTK-SIGN").Single() != expected
            || request.Headers.GetValues("X-BTK-APIKEY").Single() != "fake-key"
        )
        {
            throw new InvalidOperationException("Trading request signature mismatch.");
        }

        if (request.Method == HttpMethod.Post)
        {
            this.Posts++;
            this.LastBody = body;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                this.Response?.Invoke(uri.PathAndQuery) ?? this.Json,
                Encoding.UTF8,
                "application/json"
            ),
        };
    }
}
