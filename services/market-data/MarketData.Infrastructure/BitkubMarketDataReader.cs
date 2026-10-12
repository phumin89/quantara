using System.Globalization;
using System.Text.Json;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public sealed class BitkubMarketDataReader(HttpClient client) : IMarketDataReader
{
    public async Task<IReadOnlyList<MarketCoin>> ReadCoins(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
        using var response = await client.GetAsync("/api/v3/market/symbols", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        EnsureSuccess(document.RootElement);
        var symbols = document.RootElement.GetProperty("result");
        return symbols.EnumerateArray().Select(symbol =>
        {
            if (RequiredString(symbol, "quote_asset") != "THB")
            {
                return null;
            }

            var baseAsset = RequiredString(symbol, "base_asset");
            var name = RequiredString(symbol, "name");
            var source = RequiredString(symbol, "source");
            return new MarketCoin(baseAsset.ToUpperInvariant(), name, source);
        }).OfType<MarketCoin>().DistinctBy(coin => coin.Symbol).ToArray();
    }

    public async Task<IReadOnlyList<MarketQuote>> ReadQuotes(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
        using var response = await client.GetAsync("/api/v3/market/ticker", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.EnumerateArray().Select(item =>
        {
            var symbol = RequiredString(item, "symbol").Split('_', 2, StringSplitOptions.RemoveEmptyEntries);
            var last = ParseDecimal(item, "last");
            return symbol is { Length: 2 } && symbol[1] == "THB" && last > 0m ? new MarketQuote(symbol[0].ToUpperInvariant(), last) : null;
        }).OfType<MarketQuote>().DistinctBy(quote => quote.Symbol).ToArray();
    }

    private static string RequiredString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Bitkub response is missing {propertyName}.");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? throw new InvalidDataException($"Bitkub response is missing {propertyName}.") : text;
    }

    private static decimal ParseDecimal(JsonElement item, string propertyName)
    {
        var value = item.GetProperty(propertyName);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new InvalidDataException($"Bitkub response contains an invalid {propertyName}.");
    }

    private static void EnsureSuccess(JsonElement root)
    {
        var code = root.TryGetProperty("error", out var error) ? error.ToString()
            : root.TryGetProperty("code", out var status) ? status.ToString() : "0";
        if (code != "0")
        {
            throw new InvalidDataException("Bitkub returned an error response.");
        }
    }
}
