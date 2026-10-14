using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Checks;

public static class AdaptiveMarketChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_100);
        var flat = Create(now);
        var assessment = AdaptiveMarketEvaluator.Evaluate(flat, now);
        Assert(
            assessment.Available
                && assessment.Regime == MarketRegime.Range
                && assessment.Volatility == MarketVolatility.Normal,
            "Flat market classification failed."
        );
        Near(assessment.Ema20, 100m, "Flat EMA20");
        Near(assessment.Ema50, 100m, "Flat EMA50");
        Near(assessment.Atr14, 2m, "Flat ATR14");
        Near(assessment.RelativeAtr, 1m, "Flat relative ATR");
        Assert(
            assessment.RuleVersion == "adaptive-ema-atr-v1"
                && assessment.UsedBars == 100
                && assessment.WindowFrom == flat.Candles[0].Timestamp
                && assessment.CandleAt == flat.Candles[^1].Timestamp,
            "Assessment provenance failed."
        );

        var up = AdaptiveMarketEvaluator.Evaluate(Create(now, step: 1m), now);
        Assert(up.Regime == MarketRegime.Uptrend, "Linear uptrend failed.");
        Near(up.Ema20, 189.5m, "Linear EMA20 SMA seed");
        Near(up.Ema50, 174.5m, "Linear EMA50 SMA seed");
        Near(up.Atr14, 2m, "Linear true range");
        Near(up.TrendGapAtr, 7.5m, "Normalized EMA separation");
        Near(up.Ema20SlopeAtr, 2.5m, "Five-bar EMA slope");
        var down = AdaptiveMarketEvaluator.Evaluate(Create(now, start: 200m, step: -1m), now);
        Assert(down.Regime == MarketRegime.Downtrend, "Linear downtrend failed.");
        Near(down.Ema20, 110.5m, "Downtrend EMA20");
        Near(down.Ema50, 125.5m, "Downtrend EMA50");

        var wideInitial = With(flat, 0, flat.Candles[0] with { High = 120m, Low = 80m });
        Near(
            AdaptiveMarketEvaluator.Evaluate(wideInitial, now).Atr14,
            2m,
            "ATR seed must exclude row zero without a previous close"
        );
        var historicalShock = With(flat, 50, flat.Candles[50] with { High = 108m, Low = 92m });
        Near(
            AdaptiveMarketEvaluator.Evaluate(historicalShock, now).Atr14,
            2m + (decimal)Math.Pow(13d / 14d, 49),
            "Wilder decay after a known TR shock"
        );
        var latestShock = With(flat, 99, flat.Candles[^1] with { High = 120m, Low = 80m });
        var shocked = AdaptiveMarketEvaluator.Evaluate(latestShock, now);
        Near(shocked.Atr14, 66m / 14m, "Latest volatility shock");
        Assert(
            shocked.Regime == MarketRegime.Range && shocked.Volatility == MarketVolatility.High,
            "Volatility must not invent trend direction."
        );
        var quiet = Create(now, range: 0.1m);
        quiet = With(quiet, 99, quiet.Candles[^1] with { High = 100.1m, Low = 99.9m });
        var priorWide = flat.Candles.Take(99).Append(quiet.Candles[^1]).ToArray();
        // One narrow bar smooths ATR; a sustained compression is needed for Low.
        var compression = flat
            .Candles.Select((bar, index) => index >= 90 ? bar with { High = 100.1m, Low = 99.9m } : bar)
            .ToArray();
        Assert(
            AdaptiveMarketEvaluator.Evaluate(flat with { Candles = compression }, now).Volatility
                == MarketVolatility.Low,
            "Sustained volatility compression failed."
        );
        Assert(
            AdaptiveMarketEvaluator.Evaluate(flat with { Candles = priorWide }, now).Volatility
                == MarketVolatility.Normal,
            "One narrow bar should not imply sustained low volatility."
        );
        var zero = AdaptiveMarketEvaluator.Evaluate(Create(now, range: 0m), now);
        Assert(
            zero.Available
                && zero.Regime == MarketRegime.Range
                && zero.Volatility == MarketVolatility.Low
                && zero.Atr14 == 0m
                && zero.RelativeAtr is null
                && zero.TrendGapAtr is null
                && zero.Ema20SlopeAtr is null,
            "Zero-range arithmetic failed."
        );
        var zeroShockSeries = Create(now, range: 0m);
        zeroShockSeries = With(zeroShockSeries, 99, zeroShockSeries.Candles[^1] with { High = 120m, Low = 80m });
        var zeroShock = AdaptiveMarketEvaluator.Evaluate(zeroShockSeries, now);
        Assert(
            zeroShock.Volatility == MarketVolatility.High && zeroShock.RelativeAtr is null,
            "Zero volatility baseline must not invent a finite ratio."
        );
        var transition = With(flat, 99, flat.Candles[^1] with { Close = 150m, High = 151m, Low = 99m });
        Assert(
            AdaptiveMarketEvaluator.Evaluate(transition, now).Regime == MarketRegime.Transition,
            "A single changed regime must not switch the three-bar consensus."
        );

        foreach (
            var invalid in new[]
            {
                flat with
                {
                    Error = "Provider unavailable",
                },
                flat with
                {
                    FetchedAt = now.AddMinutes(-3),
                },
                flat with
                {
                    FetchedAt = now.AddSeconds(6),
                },
                Create(now, count: 69),
                flat with
                {
                    Candles = Array.Empty<MarketCandle>(),
                },
                With(flat, 99, flat.Candles[^1] with { Timestamp = now.ToUnixTimeSeconds() }),
                Create(now.AddMinutes(-10)) with
                {
                    FetchedAt = now,
                },
                With(flat, 99, flat.Candles[^1] with { Timestamp = flat.Candles[^1].Timestamp + 1 }),
                With(flat, 99, flat.Candles[^1] with { High = 99m }),
                With(flat, 99, flat.Candles[^1] with { Low = 101m }),
                With(flat, 99, flat.Candles[^1] with { Close = 0m }),
                With(flat, 99, flat.Candles[^1] with { Volume = -1m }),
                With(flat, 50, flat.Candles[50] with { Timestamp = flat.Candles[50].Timestamp - 300 }),
                Create(now, start: decimal.MaxValue / 2m, range: 0m),
            }
        )
        {
            Unavailable(AdaptiveMarketEvaluator.Evaluate(invalid, now));
        }

        var gapped = With(flat, 50, flat.Candles[50] with { Timestamp = flat.Candles[50].Timestamp - 300 });
        Assert(
            AdaptiveMarketEvaluator.Evaluate(gapped, now).UsedBars == 49,
            "An unavailable assessment must report its actual contiguous suffix count."
        );
        var oldGap = Create(now, count: 120);
        oldGap = With(oldGap, 5, oldGap.Candles[5] with { Timestamp = oldGap.Candles[5].Timestamp - 300, High = 0m });
        var afterGap = AdaptiveMarketEvaluator.Evaluate(oldGap, now);
        Assert(
            afterGap.Available && afterGap.UsedBars == 114 && afterGap.WindowFrom == oldGap.Candles[6].Timestamp,
            "Excluded old gaps must not invalidate an adequate later window."
        );
        Assert(
            AdaptiveMarketEvaluator.Evaluate(Create(now, count: 140), now).UsedBars == 120,
            "Indicator source window must remain bounded."
        );
        var prefixNow = DateTimeOffset.FromUnixTimeSeconds(flat.Candles[79].Timestamp + 300);
        var frozen = flat with { Candles = flat.Candles.Take(80).ToArray(), FetchedAt = prefixNow };
        var before = AdaptiveMarketEvaluator.Evaluate(frozen, prefixNow);
        Unavailable(AdaptiveMarketEvaluator.Evaluate(flat with { FetchedAt = prefixNow }, prefixNow));
        var after = AdaptiveMarketEvaluator.Evaluate(frozen, prefixNow);
        Assert(before.Available && before == after, "Future bars must not reprice a frozen assessment.");
        Console.WriteLine(
            "Adaptive checks: EMA/ATR arithmetic, regimes, volatility, warmup, gaps, timestamps, overflow and frozen windows passed."
        );
    }

    private static IntradaySeries Create(
        DateTimeOffset now,
        int count = 100,
        decimal start = 100m,
        decimal step = 0m,
        decimal range = 1m
    )
    {
        var candles = Enumerable
            .Range(0, count)
            .Select(index =>
            {
                var close = start + step * index;
                return new MarketCandle(
                    now.ToUnixTimeSeconds() - (count - index) * 300,
                    close,
                    close + range,
                    close - range,
                    close,
                    1m
                );
            })
            .ToArray();
        return new IntradaySeries("BTC_THB", candles, now, null);
    }

    private static IntradaySeries With(IntradaySeries series, int index, MarketCandle candle)
    {
        var candles = series.Candles.ToArray();
        candles[index] = candle;
        return series with { Candles = candles };
    }

    private static void Unavailable(AdaptiveMarketAssessment assessment)
    {
        Assert(
            !assessment.Available
                && assessment.Regime == MarketRegime.Unknown
                && assessment.Volatility == MarketVolatility.Unknown
                && !string.IsNullOrWhiteSpace(assessment.UnavailableReason)
                && assessment.WindowFrom is null
                && assessment.CandleAt is null
                && new[]
                {
                    assessment.Ema20,
                    assessment.Ema50,
                    assessment.Atr14,
                    assessment.AtrPercent,
                    assessment.RelativeAtr,
                    assessment.TrendGapAtr,
                    assessment.Ema20SlopeAtr,
                }.All(value => value is null),
            "Unavailable evidence must not publish invented indicator values."
        );
    }

    private static void Near(decimal? actual, decimal expected, string message) =>
        Assert(actual is decimal value && Math.Abs(value - expected) < 0.00000001m, message);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
