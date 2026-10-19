using System.Net;
using System.Text.Json;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public sealed class TradingMarketEvidenceChecks(string payload) : HttpMessageHandler
{
    public static async Task Run()
    {
        const string historyError = "Fewer than 51 consecutive recent completed candles could be verified.";
        using var handler = new TradingMarketEvidenceChecks(
            JsonSerializer.Serialize(
                new { markets = Array.Empty<object>(), errors = new[] { new { symbol = "ETH", error = historyError } } }
            )
        );
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://test.invalid/") };
        var result = await new TradingMarketReader(client).ReadEvidence(["ETH"], CancellationToken.None);
        if (result.Books.Count != 0 || result.Errors.GetValueOrDefault("ETH") != historyError)
        {
            throw new InvalidOperationException("Transport must retain the verified per-symbol history failure.");
        }

        foreach (
            var errors in new[]
            {
                "[]",
                "[{\"symbol\":\"BTC\",\"error\":\"missing\"}]",
                "[{\"symbol\":\"ETH\",\"error\":\"missing\"},{\"symbol\":\"ETH\",\"error\":\"duplicate\"}]",
            }
        )
        {
            using var invalidHandler = new TradingMarketEvidenceChecks("{\"markets\":[],\"errors\":" + errors + "}");
            using var invalidClient = new HttpClient(invalidHandler) { BaseAddress = new Uri("http://test.invalid/") };
            var rejected = false;
            try
            {
                await new TradingMarketReader(invalidClient).ReadEvidence(["ETH"], CancellationToken.None);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new InvalidOperationException(
                    "Missing, unexpected and duplicate evidence identities must fail closed."
                );
            }
        }

        Console.WriteLine("Trading market evidence checks passed.");
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException("Market evidence is read-only.");
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }
}
