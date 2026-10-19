using System.Text.Json;

namespace Quantara.Team.Infrastructure;

public static class NativeMarketResearch
{
    private static readonly string[] columns = ["timestamp", "open", "high", "low", "close", "volume"];

    public static object Project(JsonElement root, IReadOnlyList<string> symbols, DateTimeOffset now)
    {
        var markets = root.GetProperty("markets").EnumerateArray().ToArray();
        var errors = root.GetProperty("errors").EnumerateArray().ToArray();
        var proof = root.GetProperty("evidence").EnumerateArray().ToArray();
        var identities = markets.Concat(errors).Select(item => item.GetProperty("symbol").GetString()).ToArray();
        if (
            identities.Length != symbols.Count
            || identities.Distinct(StringComparer.Ordinal).Count() != identities.Length
            || identities.Any(symbol => symbol is null || !symbols.Contains(symbol, StringComparer.Ordinal))
            || proof.Length > symbols.Count * 3
        )
        {
            throw new InvalidDataException("Native research identities are inconsistent.");
        }

        foreach (var item in proof)
        {
            var interval = item.GetProperty("timeframeSeconds").GetInt32();
            var at = item.GetProperty("checkedAt").GetDateTimeOffset();
            if (
                !symbols.Contains(item.GetProperty("symbol").GetString(), StringComparer.Ordinal)
                || interval is not (300 or 900 or 3600)
                || item.GetProperty("source").GetString() != "bitkub-tradingview"
                || at > now.AddSeconds(5)
                || now - at > TimeSpan.FromSeconds(120)
                || item.GetProperty("status").GetString() is not ("ready" or "unavailable")
                || item.GetProperty("completedBars").GetInt32() is < 0 or > 120
                || item.GetProperty("contiguousBars").GetInt32() is < 0 or > 120
            )
            {
                throw new InvalidDataException("Native research provenance is invalid.");
            }
        }

        var projected = new List<object>();
        foreach (var market in markets)
        {
            var symbol = market.GetProperty("symbol").GetString();
            var interval = market.GetProperty("timeframeSeconds").GetInt32();
            var observedAt = market.GetProperty("observedAt").GetDateTimeOffset();
            var evidence = proof.SingleOrDefault(item =>
                item.GetProperty("symbol").GetString() == symbol
                && item.GetProperty("timeframeSeconds").GetInt32() == interval
                && item.GetProperty("status").GetString() == "ready"
            );
            var bars = market.GetProperty("bars").EnumerateArray().ToArray();
            if (
                interval is not (300 or 900 or 3600)
                || evidence.ValueKind == JsonValueKind.Undefined
                || bars.Length is < 51 or > 120
                || evidence.GetProperty("contiguousBars").GetInt32() != bars.Length
                || observedAt > now.AddSeconds(5)
                || now - observedAt > TimeSpan.FromSeconds(120)
            )
            {
                throw new InvalidDataException("Native research requires verified completed history and fresh quotes.");
            }

            var checkedAt = evidence.GetProperty("checkedAt").GetDateTimeOffset();
            var end = checkedAt.ToUnixTimeSeconds() / interval * interval;
            var candles = new List<decimal[]>();
            long? previous = null;
            foreach (var bar in bars)
            {
                var timestamp = bar.GetProperty("timestamp").GetInt64();
                var open = Number(bar, "open");
                var high = Number(bar, "high");
                var low = Number(bar, "low");
                var close = Number(bar, "close");
                var volume = Number(bar, "volume");
                if (
                    timestamp % interval != 0
                    || previous is { } earlier && timestamp - earlier != interval
                    || open <= 0
                    || low <= 0
                    || close <= 0
                    || volume < 0
                    || high < Math.Max(open, close)
                    || low > Math.Min(open, close)
                )
                {
                    throw new InvalidDataException("Native candles must be lossless, valid and continuous.");
                }

                previous = timestamp;
                candles.Add([timestamp, open, high, low, close, volume]);
            }

            if (previous != end - interval)
            {
                throw new InvalidDataException("Native history does not end at its latest completed close.");
            }

            var bid = Number(market, "bid");
            var ask = Number(market, "ask");
            if (
                bid <= 0
                || ask < bid
                || Number(market, "minimumNotional") <= 0
                || Number(market, "priceStep") <= 0
                || Number(market, "bidQuantity") < 0
                || Number(market, "askQuantity") < 0
            )
            {
                throw new InvalidDataException("Native market rules or liquidity are invalid.");
            }

            projected.Add(
                new
                {
                    symbol,
                    observedAt,
                    timeframeSeconds = interval,
                    bid,
                    ask,
                    bidQuantity = Number(market, "bidQuantity"),
                    askQuantity = Number(market, "askQuantity"),
                    minimumNotional = Number(market, "minimumNotional"),
                    priceStep = Number(market, "priceStep"),
                    priceScale = market.GetProperty("priceScale").GetInt32(),
                    baseScale = market.GetProperty("baseScale").GetInt32(),
                    quoteScale = market.GetProperty("quoteScale").GetInt32(),
                    enabled = market.GetProperty("enabled").GetBoolean(),
                    totalBars = bars.Length,
                    returnedBars = Math.Min(51, bars.Length),
                    candles = candles.TakeLast(51).ToArray(),
                }
            );
        }

        return new
        {
            available = projected.Count > 0,
            executionAuthority = false,
            source = "bitkub-tradingview",
            candleColumns = NativeMarketResearch.columns,
            markets = projected,
            errors = errors
                .Select(item => new { symbol = item.GetProperty("symbol").GetString(), error = Text(item, "error") })
                .ToArray(),
            evidence = proof
                .Select(item => new
                {
                    symbol = item.GetProperty("symbol").GetString(),
                    timeframeSeconds = item.GetProperty("timeframeSeconds").GetInt32(),
                    checkedAt = item.GetProperty("checkedAt").GetDateTimeOffset(),
                    source = "bitkub-tradingview",
                    status = item.GetProperty("status").GetString(),
                    contiguousBars = item.GetProperty("contiguousBars").GetInt32(),
                    reason = Text(item, "reason"),
                })
                .ToArray(),
        };
    }

    private static decimal Number(JsonElement value, string name)
    {
        return value.GetProperty(name).GetDecimal();
    }

    private static string? Text(JsonElement value, string name)
    {
        var item = value.GetProperty(name);
        return item.ValueKind == JsonValueKind.Null ? null
            : item.GetString() is { Length: <= 200 } text ? text
            : throw new InvalidDataException("Native research error is invalid.");
    }
}
