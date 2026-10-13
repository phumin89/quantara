using System.Globalization;
using System.Text.Json;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public static class PublicTickerParser
{
    public static LiveMarketQuote? Parse(string json, DateTimeOffset observedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var stream = root.GetProperty("stream").GetString();
            const string prefix = "market.ticker.thb_";
            if (stream is null || !stream.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var coin = stream[prefix.Length..].ToUpperInvariant();
            var bid = PublicTickerParser.Number(root, "highestBid");
            var ask = PublicTickerParser.Number(root, "lowestAsk");
            var last = PublicTickerParser.Number(root, "last");
            if (
                coin.Length is < 1 or > 30
                || !coin.All(char.IsAsciiLetterOrDigit)
                || bid <= 0
                || ask < bid
                || last <= 0
                || ask > 1000000000000m
                || last > 1000000000000m
                || PublicTickerParser.Number(root, "isFrozen") != 0
            )
            {
                return null;
            }

            return new LiveMarketQuote(coin + "_THB", bid, ask, last, observedAt, null);
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or InvalidOperationException
                        or FormatException
                        or KeyNotFoundException
                        or OverflowException
            )
        {
            return null;
        }
    }

    private static decimal Number(JsonElement root, string field) =>
        decimal.Parse(root.GetProperty(field).ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
