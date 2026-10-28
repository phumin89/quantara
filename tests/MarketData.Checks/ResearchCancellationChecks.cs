using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class ResearchCancellationChecks
{
    public static async Task Run()
    {
        foreach (var endpoint in new[] { "depth", "trades" })
        {
            using var cancellation = new CancellationTokenSource();
            var handler = new ResearchHttpHandler
            {
                BeforeResponse = (uri, token) =>
                {
                    if (uri.AbsolutePath.EndsWith(endpoint, StringComparison.Ordinal))
                    {
                        cancellation.Cancel();
                        token.ThrowIfCancellationRequested();
                    }
                },
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
            var reader = new BitkubResearchMarketReader(client);

            var canceled = false;
            try
            {
                await reader.Read(cancellation.Token, ["ETH"]);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            if (
                !canceled
                || handler.Requests.Count == 0
                || !handler.Requests[^1].AbsolutePath.EndsWith(endpoint, StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException($"Cancellation must stop after {endpoint}.");
            }
        }

        var optionalFailureHandler = new ResearchHttpHandler
        {
            BeforeResponse = (uri, _) =>
            {
                if (uri.AbsolutePath.EndsWith("depth", StringComparison.Ordinal))
                {
                    throw new TaskCanceledException("optional source timeout");
                }
            },
        };
        using var optionalClient = new HttpClient(optionalFailureHandler)
        {
            BaseAddress = new Uri("https://test.invalid"),
        };
        using var document = JsonDocument.Parse(
            await new BitkubResearchMarketReader(optionalClient).Read(CancellationToken.None, ["ETH"])
        );
        var market = document.RootElement.GetProperty("markets")[0];
        if (
            !document.RootElement.GetProperty("available").GetBoolean()
            || market.GetProperty("depth").ValueKind != JsonValueKind.Null
            || market.GetProperty("trades").ValueKind == JsonValueKind.Null
        )
        {
            throw new InvalidOperationException("Optional source cancellation must not cancel the request.");
        }
    }
}
