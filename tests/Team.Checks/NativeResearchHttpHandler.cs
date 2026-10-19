using System.Net;
using System.Text.Json;

namespace Team.Checks;

public sealed class NativeResearchHttpHandler : HttpMessageHandler
{
    private static readonly string[] symbols = ["ETH_THB"];

    private static readonly decimal[] prices = [100m];

    private static readonly bool[] usable = [true];

    public int NativeCalls { get; private set; }

    public bool InvalidSource { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.Method != HttpMethod.Get || request.Headers.Contains("X-Quantara-Trading-Control"))
        {
            throw new InvalidOperationException("Research is GET-only and has no trading authority.");
        }

        var now = DateTimeOffset.UtcNow;
        string body;
        if (request.RequestUri?.AbsolutePath == "/api/market/trading")
        {
            this.NativeCalls++;
            if (!request.RequestUri.Query.Contains("adaptiveTimeframes=true", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Native research must request the versioned native data path.");
            }

            var end = now.ToUnixTimeSeconds() / 900 * 900;
            body = JsonSerializer.Serialize(
                new
                {
                    markets = new[]
                    {
                        new
                        {
                            symbol = "ETH",
                            observedAt = now,
                            timeframeSeconds = 900,
                            bid = 100m,
                            ask = 100.1m,
                            bidQuantity = 100m,
                            askQuantity = 100m,
                            minimumNotional = 10m,
                            priceStep = 0.01m,
                            priceScale = 2,
                            baseScale = 8,
                            quoteScale = 2,
                            enabled = true,
                            bars = Enumerable
                                .Range(0, 51)
                                .Select(index => new
                                {
                                    timestamp = end - (51 - index) * 900,
                                    open = 100m,
                                    high = 101m,
                                    low = 99m,
                                    close = 100m,
                                    volume = 1m,
                                })
                                .ToArray(),
                        },
                    },
                    errors = Array.Empty<object>(),
                    evidence = new[]
                    {
                        new
                        {
                            symbol = "ETH",
                            checkedAt = now,
                            source = this.InvalidSource ? "unverified" : "bitkub-tradingview",
                            timeframeSeconds = 900,
                            completedBars = 51,
                            contiguousBars = 51,
                            missingIntervals = 69,
                            status = "ready",
                            gapCause = "unknown",
                            reason = (string?)null,
                        },
                    },
                }
            );
        }
        else
        {
            body = JsonSerializer.Serialize(
                new
                {
                    available = true,
                    fetchedAt = now,
                    scan = new
                    {
                        symbols = NativeResearchHttpHandler.symbols,
                        last = NativeResearchHttpHandler.prices,
                        usable = NativeResearchHttpHandler.usable,
                    },
                    markets = Array.Empty<object>(),
                }
            );
        }

        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            }
        );
    }
}
