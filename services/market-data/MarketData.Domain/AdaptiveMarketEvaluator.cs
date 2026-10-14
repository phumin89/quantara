namespace Quantara.MarketData.Domain;

public static class AdaptiveMarketEvaluator
{
    public const string RuleVersion = "adaptive-ema-atr-v1";

    public static AdaptiveMarketAssessment Evaluate(IntradaySeries series, DateTimeOffset now)
    {
        var suffix = series.Candles.TakeLast(120).ToArray();
        if (series.Error is not null)
        {
            return Unavailable(suffix.Length, series.Error);
        }

        if (series.FetchedAt > now.AddSeconds(5) || now - series.FetchedAt > TimeSpan.FromMinutes(2))
        {
            return Unavailable(suffix.Length, "series timestamp is stale or in the future");
        }

        var contiguous = suffix.Length == 0 ? suffix : suffix.TakeLast(ContiguousCount(suffix)).ToArray();
        if (contiguous.Length < 70)
        {
            return Unavailable(contiguous.Length, "fewer than 70 contiguous completed bars");
        }

        var reason = Validate(contiguous, now);
        if (reason is not null)
        {
            return Unavailable(contiguous.Length, reason);
        }

        try
        {
            var closes = contiguous.Select(c => c.Close).ToArray();
            var ema20 = Ema(closes, 20);
            var ema50 = Ema(closes, 50);
            var tr = new decimal[closes.Length];
            for (var i = 0; i < closes.Length; i++)
            {
                tr[i] =
                    i == 0
                        ? contiguous[i].High - contiguous[i].Low
                        : Math.Max(
                            contiguous[i].High - contiguous[i].Low,
                            Math.Max(
                                Math.Abs(contiguous[i].High - closes[i - 1]),
                                Math.Abs(contiguous[i].Low - closes[i - 1])
                            )
                        );
            }

            var atr = Wilder(tr, 14);
            var latest = closes.Length - 1;
            var currentAtr = atr[latest];
            var prior = atr.Skip(Math.Max(0, latest - 20)).Take(Math.Min(20, latest)).OrderBy(x => x).ToArray();
            var median =
                prior.Length == 0 ? 0m
                : prior.Length % 2 == 1 ? prior[prior.Length / 2]
                : (prior[prior.Length / 2 - 1] + prior[prior.Length / 2]) / 2m;
            decimal? relative = median == 0m ? null : currentAtr / median;
            var volatility =
                median == 0m ? (currentAtr > 0m ? MarketVolatility.High : MarketVolatility.Low)
                : relative >= 1.5m ? MarketVolatility.High
                : relative <= .75m ? MarketVolatility.Low
                : MarketVolatility.Normal;
            var gap = currentAtr == 0m ? (decimal?)null : (ema20[latest] - ema50[latest]) / currentAtr;
            var slope = currentAtr == 0m ? (decimal?)null : (ema20[latest] - ema20[latest - 5]) / currentAtr;
            var regimes = Enumerable
                .Range(0, 3)
                .Select(offset =>
                    Classify(
                        ema20[latest - offset],
                        ema50[latest - offset],
                        ema20[latest - offset] - ema20[latest - offset - 5],
                        atr[latest - offset]
                    )
                )
                .ToArray();
            var regime = regimes.All(x => x == regimes[0]) ? regimes[0] : MarketRegime.Transition;
            return new AdaptiveMarketAssessment(
                true,
                RuleVersion,
                regime,
                volatility,
                contiguous.Length,
                contiguous[0].Timestamp,
                contiguous[^1].Timestamp,
                ema20[latest],
                ema50[latest],
                currentAtr,
                closes[latest] == 0m ? null : currentAtr / closes[latest] * 100m,
                relative,
                gap,
                slope,
                null
            );
        }
        catch (OverflowException)
        {
            return Unavailable(contiguous.Length, "indicator arithmetic overflow");
        }
    }

    private static AdaptiveMarketAssessment Unavailable(int bars, string reason) =>
        new(
            false,
            RuleVersion,
            MarketRegime.Unknown,
            MarketVolatility.Unknown,
            bars,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            reason
        );

    private static string? Validate(MarketCandle[] candles, DateTimeOffset now)
    {
        for (var i = 0; i < candles.Length; i++)
        {
            var c = candles[i];
            if (
                c.Timestamp <= 0
                || c.Timestamp % 300 != 0
                || c.Open <= 0
                || c.High <= 0
                || c.Low <= 0
                || c.Close <= 0
                || c.Volume < 0
                || c.High < Math.Max(c.Open, c.Close)
                || c.Low > Math.Min(c.Open, c.Close)
                || c.Low <= 0
                || (i > 0 && c.Timestamp <= candles[i - 1].Timestamp)
            )
            {
                return "malformed candle";
            }

            if (
                i == candles.Length - 1
                && (c.Timestamp > now.ToUnixTimeSeconds() - 300 || c.Timestamp < now.ToUnixTimeSeconds() - 600)
            )
            {
                return "latest candle is unfinished or outside freshness window";
            }
        }

        return null;
    }

    private static int ContiguousCount(MarketCandle[] candles)
    {
        var count = 1;
        for (var i = candles.Length - 1; i > 0 && candles[i].Timestamp - candles[i - 1].Timestamp == 300; i--)
        {
            count++;
        }

        return count;
    }

    private static decimal[] Ema(decimal[] values, int period)
    {
        var result = new decimal[values.Length];
        result[period - 1] = values.Take(period).Average();
        var alpha = 2m / (period + 1);
        for (var i = period; i < values.Length; i++)
        {
            result[i] = alpha * values[i] + (1m - alpha) * result[i - 1];
        }

        return result;
    }

    private static decimal[] Wilder(decimal[] values, int period)
    {
        var result = new decimal[values.Length];
        result[period] = values.Skip(1).Take(period).Average();
        for (var i = period + 1; i < values.Length; i++)
        {
            result[i] = (result[i - 1] * (period - 1) + values[i]) / period;
        }

        return result;
    }

    private static MarketRegime Classify(decimal fast, decimal slow, decimal slopeRaw, decimal atr)
    {
        if (atr == 0m)
        {
            return MarketRegime.Range;
        }

        var gap = (fast - slow) / atr;
        var slope = slopeRaw / atr;
        if (gap >= .5m && slope > 0m)
        {
            return MarketRegime.Uptrend;
        }

        if (gap <= -.5m && slope < 0m)
        {
            return MarketRegime.Downtrend;
        }

        if (Math.Abs(gap) <= .25m && Math.Abs(slope) <= .25m)
        {
            return MarketRegime.Range;
        }

        return MarketRegime.Transition;
    }
}
