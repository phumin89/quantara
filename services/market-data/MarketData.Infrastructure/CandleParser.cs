using System.Text.Json;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public static class CandleParser
{
    private static readonly string[] valueFields = ["o", "h", "l", "c", "v"];

    public static IReadOnlyList<MarketCandle> Parse(
        JsonElement root,
        long from,
        long to,
        long intervalSeconds = 86400,
        int maxRows = 200
    )
    {
        if (root.GetProperty("s").GetString() != "ok")
        {
            throw new InvalidDataException("Historical candles are unavailable.");
        }

        var timestamps = root.GetProperty("t");
        var count = timestamps.GetArrayLength();
        if (
            intervalSeconds is not (300 or 900 or 3600 or 86400)
            || maxRows is < 1 or > 1000
            || count > maxRows
            || CandleParser.valueFields.Any(field => root.GetProperty(field).GetArrayLength() != count)
        )
        {
            throw new InvalidDataException("Historical candle arrays are inconsistent.");
        }

        var candles = new List<MarketCandle>();
        for (var index = 0; index < count; index++)
        {
            var candle = new MarketCandle(
                timestamps[index].GetInt64(),
                root.GetProperty("o")[index].GetDecimal(),
                root.GetProperty("h")[index].GetDecimal(),
                root.GetProperty("l")[index].GetDecimal(),
                root.GetProperty("c")[index].GetDecimal(),
                root.GetProperty("v")[index].GetDecimal()
            );
            if (candle.Timestamp < from || candle.Timestamp > to - intervalSeconds)
            {
                continue;
            }

            if (intervalSeconds < 86400 && candle.Timestamp % intervalSeconds != 0)
            {
                throw new InvalidDataException("Intraday candle timestamp is not aligned to its native timeframe.");
            }

            if (
                candle.Open <= 0
                || candle.Close <= 0
                || candle.Low <= 0
                || candle.Volume < 0
                || candle.High < decimal.Max(candle.Open, candle.Close)
                || candle.Low > decimal.Min(candle.Open, candle.Close)
                || candle.High < candle.Low
            )
            {
                throw new InvalidDataException("Historical OHLC values are inconsistent.");
            }

            candles.Add(candle);
        }

        // Conflicting duplicates are excluded; do not choose an arbitrary price.
        return candles
            .GroupBy(candle => candle.Timestamp)
            .Where(group => group.Distinct().Count() == 1)
            .Select(group => group.First())
            .OrderBy(candle => candle.Timestamp)
            .ToArray();
    }
}
