using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Checks;

public static class IntradaySignalChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000);
        var breakout = CreateSeries(now, 1_000m, 1_200m, 1_000m);
        var quote = new LiveMarketQuote("BTC", 1_199m, 1_201m, 1_200m, now.AddSeconds(-5), null);
        var signal = IntradaySignalEvaluator.Evaluate(quote, breakout, now);
        Assert(signal?.Direction == SignalDirection.Breakout, "breakout failed");
        var signalId = signal?.Id ?? throw new InvalidOperationException("breakout did not produce a signal");
        Assert(
            IntradaySignalEvaluator.Evaluate(quote, breakout, now.AddSeconds(1))?.Id == signalId,
            "signal id changed"
        );
        Assert(
            IntradaySignalEvaluator.Evaluate(quote, CreateSeries(now, 1_000m, 800m, 1_000m), now)?.Direction
                == SignalDirection.Breakdown,
            "breakdown failed"
        );
        Assert(
            IntradaySignalEvaluator.Evaluate(quote, CreateSeries(now, 1_000m, 1_000m, 1_000m), now) is null,
            "exact level alerted"
        );
        Assert(
            IntradaySignalEvaluator.Evaluate(quote, CreateSeries(now, 1_000m, 1_200m, 0.5m), now) is null,
            "low volume alerted"
        );
        Assert(
            IntradaySignalEvaluator.Evaluate(
                new LiveMarketQuote("BTC", 1_000m, 1_006m, 1_003m, now, null),
                breakout,
                now
            )
                is null,
            "wide spread alerted"
        );
        Assert(
            IntradaySignalEvaluator.Evaluate(quote with { ObservedAt = now.AddSeconds(-61) }, breakout, now) is null,
            "stale quote alerted"
        );
        Assert(
            IntradaySignalEvaluator.Evaluate(
                quote,
                breakout with
                {
                    FetchedAt = now.AddMinutes(-2).AddSeconds(-1),
                },
                now
            )
                is null,
            "stale history alerted"
        );
        Assert(IntradaySignalEvaluator.Evaluate(quote, WithCandle(breakout, 1, 300), now) is null, "gap alerted");
        Assert(
            IntradaySignalEvaluator.Evaluate(quote, WithCandle(breakout, 20, now.ToUnixTimeSeconds()), now) is null,
            "incomplete candle alerted"
        );
        Assert(
            IntradaySignalEvaluator.Evaluate(
                quote,
                WithCandle(breakout, 20, now.ToUnixTimeSeconds() - 300, high: 0m),
                now
            )
                is null,
            "malformed candle alerted"
        );
        var shifted = breakout with
        {
            Candles = breakout.Candles.Select(candle => candle with { Timestamp = candle.Timestamp + 1 }).ToArray(),
        };
        Assert(IntradaySignalEvaluator.Evaluate(quote, shifted, now) is null, "shifted history alerted");
        Assert(
            SafeEvaluate(quote, CreateVolumeSeries(now, decimal.MaxValue, decimal.MaxValue)) is null,
            "median overflow alerted"
        );
        Assert(
            SafeEvaluate(quote, CreateVolumeSeries(now, decimal.MaxValue / 4m, decimal.MaxValue / 2m))?.Direction
                == SignalDirection.Breakout,
            "large volume valid breakout failed"
        );
        Assert(
            SafeEvaluate(quote, CreateVolumeSeries(now, 0.0000000000000000000000000001m, decimal.MaxValue)) is null,
            "volume ratio overflow alerted"
        );
        Assert(
            SafeEvaluate(quote, CreateSeries(now, 1_000m, 1_200m, 1_000m))?.Direction == SignalDirection.Breakout,
            "ordinary breakout after invalid cases failed"
        );
        Console.WriteLine("Intraday signal checks passed.");
    }

    private static IntradaySeries CreateSeries(DateTimeOffset now, decimal baseline, decimal lastClose, decimal volume)
    {
        var candles = Enumerable
            .Range(0, 21)
            .Select(index => new MarketCandle(
                (now.ToUnixTimeSeconds() / 300) * 300 - (21 - index) * 300,
                baseline,
                baseline,
                baseline,
                baseline,
                1m
            ))
            .ToArray();
        candles[^1] = candles[^1] with
        {
            Open = baseline,
            High = Math.Max(baseline, lastClose),
            Low = Math.Min(baseline, lastClose),
            Close = lastClose,
            Volume = volume,
        };
        return new IntradaySeries("BTC", candles, now.AddSeconds(-1), null);
    }

    private static IntradaySeries CreateVolumeSeries(DateTimeOffset now, decimal priorVolume, decimal latestVolume)
    {
        var series = CreateSeries(now, 1_000m, 1_200m, latestVolume);
        return series with
        {
            Candles = series
                .Candles.Select(
                    (candle, index) => index == series.Candles.Count - 1 ? candle : candle with { Volume = priorVolume }
                )
                .ToArray(),
        };
    }

    private static MarketSignal? SafeEvaluate(LiveMarketQuote quote, IntradaySeries series)
    {
        try
        {
            return IntradaySignalEvaluator.Evaluate(quote, series, series.FetchedAt.AddSeconds(1));
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Intraday evaluation threw for invalid arithmetic input.", error);
        }
    }

    private static IntradaySeries WithCandle(IntradaySeries series, int index, long timestamp, decimal? high = null)
    {
        var candles = series.Candles.ToArray();
        candles[index] = candles[index] with { Timestamp = timestamp, High = high ?? candles[index].High };
        return series with { Candles = candles };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
