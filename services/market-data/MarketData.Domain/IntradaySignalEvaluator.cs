namespace Quantara.MarketData.Domain;

public static class IntradaySignalEvaluator
{
    private const string ruleVersion = "range20-volume1.5-spread0.5-v1";

    private const int barSeconds = 300;

    public static MarketSignal? Evaluate(LiveMarketQuote quote, IntradaySeries series, DateTimeOffset now)
    {
        if (!IsQuoteUsable(quote, now) || !IsSeriesUsable(quote, series, now))
        {
            return null;
        }

        var candles = series.Candles;
        var latest = candles[^1];
        var prior = candles.Skip(candles.Count - 21).Take(20).ToArray();
        var priorHigh = prior.Max(candle => candle.High);
        var priorLow = prior.Min(candle => candle.Low);
        var medianVolume = Median(prior.Select(candle => candle.Volume).ToArray());
        if (medianVolume <= 0m || latest.Volume < medianVolume * 1.5m)
        {
            return null;
        }

        var direction =
            latest.Close > priorHigh ? SignalDirection.Breakout
            : latest.Close < priorLow ? SignalDirection.Breakdown
            : (SignalDirection?)null;
        if (direction is null)
        {
            return null;
        }

        var mid = quote.Bid / 2m + quote.Ask / 2m;
        var spreadPercent = (quote.Ask - quote.Bid) / mid * 100m;
        var volumeRatio = latest.Volume / medianVolume;
        var signalDirection = direction.Value;
        var signalId = FormattableString.Invariant(
            $"{ruleVersion}|{series.Symbol}|{latest.Timestamp}|{signalDirection}"
        );
        return new MarketSignal(
            signalId,
            series.Symbol,
            signalDirection,
            latest.Timestamp,
            now,
            latest.Close,
            signalDirection == SignalDirection.Breakout ? priorHigh : priorLow,
            volumeRatio,
            spreadPercent,
            ruleVersion
        );
    }

    private static bool IsQuoteUsable(LiveMarketQuote quote, DateTimeOffset now)
    {
        if (
            !string.Equals(quote.Symbol, quote.Symbol.Trim(), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(quote.Symbol)
            || quote.Bid <= 0m
            || quote.Ask < quote.Bid
            || quote.Last <= 0m
            || quote.ObservedAt > now.AddSeconds(5)
            || now - quote.ObservedAt > TimeSpan.FromSeconds(60)
        )
        {
            return false;
        }

        var mid = quote.Bid / 2m + quote.Ask / 2m;
        return mid > 0m && (quote.Ask - quote.Bid) / mid * 100m <= 0.5m;
    }

    private static bool IsSeriesUsable(LiveMarketQuote quote, IntradaySeries series, DateTimeOffset now)
    {
        if (
            !string.Equals(series.Symbol, quote.Symbol, StringComparison.Ordinal)
            || !string.Equals(series.Symbol, series.Symbol.Trim(), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(series.Symbol)
            || series.Error is not null
            || series.FetchedAt > now.AddSeconds(5)
            || now - series.FetchedAt > TimeSpan.FromMinutes(2)
            || series.Candles.Count < 21
        )
        {
            return false;
        }

        var nowUnix = now.ToUnixTimeSeconds();
        var firstIndex = series.Candles.Count - 21;
        for (var index = firstIndex; index < series.Candles.Count; index++)
        {
            var candle = series.Candles[index];
            if (
                candle.Timestamp <= 0
                || candle.Open <= 0m
                || candle.High <= 0m
                || candle.Low <= 0m
                || candle.Close <= 0m
                || candle.Volume < 0m
                || candle.High < candle.Open
                || candle.High < candle.Close
                || candle.Low > candle.Open
                || candle.Low > candle.Close
                || candle.Timestamp > nowUnix - barSeconds
                || (index > firstIndex && candle.Timestamp != series.Candles[index - 1].Timestamp + barSeconds)
            )
            {
                return false;
            }
        }

        var latestTimestamp = series.Candles[^1].Timestamp;
        return latestTimestamp >= nowUnix - 600;
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2m : sorted[middle];
    }
}
