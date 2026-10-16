using System.Net;
using System.Text;

namespace Team.Checks;

public sealed class SnapshotHttpHandler : HttpMessageHandler
{
    private readonly string json;

    private readonly string? history;

    private readonly string account;

    private readonly string walletEvidence;

    private readonly string orders;

    private readonly string trading;

    public int TradingReads { get; private set; }

    public SnapshotHttpHandler(
        string json,
        string? history = null,
        string? account = null,
        string? walletEvidence = null,
        string? orders = null,
        string? trading = null
    )
    {
        this.json = json;
        this.history = history;
        this.orders = orders ?? "{\"proposals\":[]}";
        this.trading = trading ?? $"{{\"accountKey\":\"{new string('a', 64)}\",\"mode\":\"paper\"}}";
        this.account =
            account ?? "{\"accountKey\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}";
        this.walletEvidence =
            walletEvidence
            ?? $"{{\"accountKey\":\"{new string('a', 64)}\",\"observedAt\":\"{DateTimeOffset.UtcNow:O}\",\"balances\":[{{\"symbol\":\"THB\",\"quantity\":1000,\"available\":700}},{{\"symbol\":\"BTC\",\"quantity\":1,\"available\":1}}],\"fee\":{{\"rate\":0.0025,\"status\":\"estimated\",\"source\":\"test\"}}}}";
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.RequestUri?.AbsolutePath == "/api/trading")
        {
            this.TradingReads += 1;
            if (request.Method != HttpMethod.Get || request.Headers.Contains("X-Quantara-Trading-Control"))
            {
                throw new InvalidOperationException("Research trading-mode reads must carry no control authority.");
            }
        }

        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri?.AbsolutePath.EndsWith("/account/evidence", StringComparison.Ordinal) == true
                            ? this.walletEvidence
                        : request.RequestUri?.AbsolutePath.EndsWith("/account", StringComparison.Ordinal) == true
                            ? this.account
                        : request.RequestUri?.AbsolutePath.EndsWith("/history", StringComparison.Ordinal) == true
                            ? this.history ?? "{}"
                        : request.RequestUri?.AbsolutePath.EndsWith("/orders", StringComparison.Ordinal) == true
                            ? this.orders
                        : request.RequestUri?.AbsolutePath == "/api/trading" ? this.trading
                        : this.json,
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
    }
}
