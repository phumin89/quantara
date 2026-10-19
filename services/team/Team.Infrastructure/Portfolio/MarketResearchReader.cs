using System.Text.Json;
using System.Text.Json.Nodes;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class MarketResearchReader(HttpClient http) : IMarketResearchReader
{
    public Task<string> Read(string accountKey, CancellationToken cancellationToken) =>
        this.ReadForMarkets(accountKey, [], cancellationToken);

    public async Task<string> ReadForMarkets(
        string accountKey,
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken,
        bool adaptiveTimeframes = false
    )
    {
        var query = symbols.Count == 0 ? "" : "?symbols=" + Uri.EscapeDataString(string.Join(',', symbols));
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/market/research" + query);
        request.Headers.Add("X-Account-Key", accountKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 40000)
        {
            throw new InvalidOperationException("Market research snapshot is too large.");
        }

        if (!adaptiveTimeframes || symbols.Count == 0)
        {
            return body;
        }

        if (
            symbols.Count > 3
            || symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Count
            || symbols.Any(symbol =>
                symbol.Length is < 1 or > 30
                || symbol.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            )
        )
        {
            throw new ArgumentException("Native research requires up to three unique base symbols.");
        }

        object native;
        try
        {
            using var nativeResponse = await http.GetAsync(
                "api/market/trading?adaptiveTimeframes=true&symbols=" + Uri.EscapeDataString(string.Join(',', symbols)),
                cancellationToken
            );
            nativeResponse.EnsureSuccessStatusCode();
            await nativeResponse.Content.LoadIntoBufferAsync(1_000_000, cancellationToken);
            using var document = JsonDocument.Parse(await nativeResponse.Content.ReadAsStringAsync(cancellationToken));
            native = NativeMarketResearch.Project(document.RootElement, symbols, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or JsonException
                        or InvalidDataException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or FormatException
                        or OverflowException
                        or OperationCanceledException
            )
        {
            native = new
            {
                available = false,
                executionAuthority = false,
                reason = "Native candle evidence unavailable; existing market research is retained.",
            };
        }

        var combined =
            JsonNode.Parse(body)?.AsObject() ?? throw new InvalidDataException("Research object is missing.");
        combined["nativeExecution"] = JsonSerializer.SerializeToNode(native);
        if (
            combined.ToJsonString().Length > 40000
            && combined["intraday"] is JsonObject intraday
            && intraday["series"] is JsonArray series
        )
        {
            // Keep full-universe learning columns and native sizing evidence; disclose omitted duplicate 5m rows.
            foreach (var item in series.OfType<JsonObject>())
            {
                item["candles"] = new JsonArray();
                item["candlesOmitted"] = true;
            }

            intraday["candleOmissionReason"] =
                "Legacy five-minute rows omitted to fit native research. Alerts are context only; use nativeExecution bars and the saved strategy version.";
            if (intraday["signals"] is JsonArray signals)
            {
                foreach (var signal in signals.OfType<JsonObject>())
                {
                    signal["evidenceAvailable"] = false;
                }
            }
        }

        var result = combined.ToJsonString();
        return result.Length <= 50000
            ? result
            : throw new InvalidDataException("Native research exceeds its evidence budget.");
    }
}
