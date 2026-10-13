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
        Console.WriteLine("Intraday signal checks passed.");
    }

    private static IntradaySeries CreateSeries(DateTimeOffset now, decimal baseline, decimal lastClose, decimal volume)
    {
        var candles = Enumerable
            .Range(0, 21)
            .Select(index => new MarketCandle(
                now.ToUnixTimeSeconds() - (21 - index) * 300,
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
