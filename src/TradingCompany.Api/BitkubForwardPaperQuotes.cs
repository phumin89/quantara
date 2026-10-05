using System.Net.Http.Headers;
using System.Text.Json;
using TradingCompany.Application;

namespace TradingCompany.Api;

public sealed class BitkubForwardPaperQuotes(IHttpClientFactory clients, TimeProvider clock) : IForwardPaperQuotes
{
    private static readonly IReadOnlyDictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BTC-THB"] = "BTC_THB", ["ETH-THB"] = "ETH_THB", ["SOL-THB"] = "SOL_THB"
    };

    public IReadOnlyList<string> SupportedSymbols => [.. Symbols.Keys];

    public async Task<ForwardPaperQuote?> GetAsync(string symbol, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!Symbols.TryGetValue(symbol, out var market)) return null;
        var requestedAt = now;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.bitkub.com/api/v3/market/depth?sym={market}&lmt=1");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        try
        {
            using var response = await clients.CreateClient("market-data").SendAsync(request, cancellationToken);
            var receivedAt = clock.GetUtcNow();
            if (!response.IsSuccessStatusCode || receivedAt - requestedAt < TimeSpan.Zero || receivedAt - requestedAt > TimeSpan.FromSeconds(20) ||
                response.Headers.Age is { } age && age != TimeSpan.Zero || response.Headers.Date is not { } observed ||
                observed > receivedAt || receivedAt - observed > TimeSpan.FromSeconds(30)) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = json.RootElement;
            if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Number || !error.TryGetDecimal(out var errorCode) || errorCode != 0m ||
                !root.TryGetProperty("result", out var result) || !TrySide(result, "asks", out var askPrice, out var askSize) ||
                !TrySide(result, "bids", out var bidPrice, out var bidSize) || askPrice < bidPrice ||
                (askPrice - bidPrice) / ((askPrice + bidPrice) / 2m) > .02m) return null;
            return new(symbol, (bidPrice + askPrice) / 2m, "THB", 1m, observed, receivedAt, observed,
                "bitkub-public-depth-http-date-paper", FeeRate: .0025m, MaximumQuantity: Math.Min(bidSize, askSize),
                SlippageThbPerUnit: (askPrice - bidPrice) / 2m);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException or OverflowException) { return null; }
    }

    private static bool TrySide(JsonElement result, string name, out decimal price, out decimal size)
    {
        price = size = 0m;
        if (!result.TryGetProperty(name, out var side) || side.ValueKind != JsonValueKind.Array || side.GetArrayLength() != 1) return false;
        var row = side[0];
        return row.ValueKind == JsonValueKind.Array && row.GetArrayLength() == 2 &&
            row[0].ValueKind == JsonValueKind.Number && row[0].TryGetDecimal(out price) &&
            row[1].ValueKind == JsonValueKind.Number && row[1].TryGetDecimal(out size) && price > 0m && size > 0m;
    }
}
