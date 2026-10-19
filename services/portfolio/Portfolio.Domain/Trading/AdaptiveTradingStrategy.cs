using System.Globalization;

namespace Quantara.Portfolio.Domain.Trading;

public static class AdaptiveTradingStrategy
{
    private const string breakoutVersion = "breakout-v1";

    private const string pullbackVersion = "pullback-v1";

    private const string rangeVersion = "range-v1";

    private const int minimumBars = 51;

    private const int recentBars = 20;

    private const decimal minimumVolumeRatio = 1.1m;

    public static TradingDecision Evaluate(
        TradingRuleSet rules,
        TradingBook market,
        TradingHolding? holding,
        DateTimeOffset now
    )
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(market);
        var interval = TradingTimeframes.For(rules, market);
        if (
            !TradingTimeframes.Supported(interval)
            || rules.Version is not ("adaptive-intraday-v2" or "adaptive-intraday-v3") && market.TimeframeSeconds != 300
        )
        {
            return new(TradingAction.Wait, "Unsupported candle timeframe", 0, market.Bid);
        }

        var decision = EvaluateCore(rules, market, holding, now, interval);
        return decision with
        {
            TimeframeSeconds = interval,
            StrategyVersion =
                rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3"
                && decision.StrategyVersion is { } version
                    ? version.Replace("-v1", "-v2", StringComparison.Ordinal)
                        + "-"
                        + (interval / 60).ToString(CultureInfo.InvariantCulture)
                        + "m"
                    : decision.StrategyVersion,
        };
    }

    private static TradingDecision EvaluateCore(
        TradingRuleSet rules,
        TradingBook market,
        TradingHolding? holding,
        DateTimeOffset now,
        int candleSeconds
    )
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(market);

        var legacyRules = rules with
        {
            Version = "ema20-50-breakout-v1",
            Symbols = [market.Symbol],
            Adaptive = null,
            ExitPolicy = null,
        };
        legacyRules.Validate();

        var candle = market.Bars.Count == 0 ? 0L : market.Bars[^1].Timestamp;
        if (
            !(holding is null ? market.Enabled : market.SellEnabled ?? market.Enabled)
            || market.Bid <= 0m
            || market.Ask <= 0m
            || market.Ask < market.Bid
            || market.Symbol.Length == 0
            || market.PriceStep <= 0
            || market.PriceScale is < 0 or > 18
        )
        {
            return new(TradingAction.Wait, "Invalid quote or symbol", candle, 0m, StrategyVersion: breakoutVersion);
        }

        var age = now - market.ObservedAt;
        var spread = (market.Ask - market.Bid) / market.Bid * 10000m;
        if (
            age < TimeSpan.Zero
            || age.TotalSeconds > legacyRules.MaxDataAgeSeconds
            || holding is null && spread > legacyRules.MaxSpreadBps
        )
        {
            return new(
                TradingAction.Wait,
                "Stale or wide market",
                candle,
                market.Bid,
                StrategyVersion: breakoutVersion
            );
        }

        if (holding is not null)
        {
            if (rules.Version == "adaptive-intraday-v3")
            {
                if (holding.ExitPlan is null)
                {
                    return new(TradingAction.Wait, "No installed adaptive exit plan", candle, market.Bid);
                }

                var assessment = AdaptiveExitStrategy.Advance(rules, market, holding, now);
                return new(assessment.Action, assessment.Reason, candle, market.Bid);
            }

            if (holding.Symbol != market.Symbol || holding.Quantity <= 0 || holding.CostThb <= 0)
            {
                return new(TradingAction.Wait, "Invalid holding", candle, market.Bid);
            }

            if (holding.StopPrice is { } stop && market.Bid <= stop)
            {
                return new(TradingAction.Sell, "Structural invalidation", candle, market.Bid);
            }

            if (holding.TargetPrice is { } target && market.Bid >= target)
            {
                return new(TradingAction.Sell, "Volatility take profit", candle, market.Bid);
            }

            if (
                holding.TrailingDistance is { } distance
                && holding.PeakBid > holding.AverageCost
                && market.Bid <= holding.PeakBid - distance
            )
            {
                return new(TradingAction.Sell, "Volatility trailing stop", candle, market.Bid);
            }

            if (holding.StopPrice is not null)
            {
                return now - holding.OpenedAt >= TimeSpan.FromMinutes(rules.TimeExitMinutes)
                    ? new(TradingAction.Sell, "Time exit", candle, market.Bid)
                    : new(TradingAction.Wait, "Position monitored", candle, market.Bid);
            }

            return TradingStrategy.EvaluateExit(legacyRules, market, holding, now) with
            {
                StrategyVersion = breakoutVersion,
            };
        }

        if (market.Bars.Count < minimumBars)
        {
            return new(
                TradingAction.Wait,
                "Insufficient completed candles",
                candle,
                market.Bid,
                StrategyVersion: breakoutVersion
            );
        }

        var bars = market.Bars.OrderBy(bar => bar.Timestamp).ToArray();
        for (var index = 0; index < bars.Length; index++)
        {
            var bar = bars[index];
            if (
                bar.Timestamp % candleSeconds != 0
                || (index > 0 && bar.Timestamp - bars[index - 1].Timestamp != candleSeconds)
                || bar.Open <= 0m
                || bar.Close <= 0m
                || bar.High < Math.Max(bar.Open, bar.Close)
                || bar.Low > Math.Min(bar.Open, bar.Close)
                || bar.Low <= 0m
                || bar.High < bar.Low
                || (
                    rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3"
                        ? bar.Volume < 0m
                        : bar.Volume <= 0m
                )
            )
            {
                return new(
                    TradingAction.Wait,
                    "Malformed or gapped candles",
                    bar.Timestamp,
                    market.Bid,
                    StrategyVersion: breakoutVersion
                );
            }
        }

        var expectedLatest = now.ToUnixTimeSeconds() / candleSeconds * candleSeconds - candleSeconds;
        var latest = bars[^1];
        if (latest.Timestamp != expectedLatest)
        {
            return new(
                TradingAction.Wait,
                "Incomplete or stale candle",
                latest.Timestamp,
                market.Bid,
                StrategyVersion: breakoutVersion
            );
        }

        var previous = bars[^21..^1];
        var recent = bars[^recentBars..];
        var sortedVolumes = previous.Select(bar => bar.Volume).OrderBy(volume => volume).ToArray();
        var medianVolume = (sortedVolumes[9] + sortedVolumes[10]) / 2m;
        if (medianVolume <= 0)
        {
            return new(
                TradingAction.Wait,
                "Insufficient verified trading volume",
                candle,
                market.Bid,
                StrategyVersion: breakoutVersion
            );
        }

        var ema20 = Ema(bars, 20);
        var ema50 = Ema(bars, 50);
        var structuralLow = recent.Min(bar => bar.Low);
        var stopPrice = structuralLow;
        var targetRoom = latest.Close * legacyRules.TakeProfitBps / 10000m;
        var costHurdle = market.Ask * (legacyRules.FeeBps * 2m + spread + legacyRules.SlippageBps) / 10000m;
        if (targetRoom <= costHurdle || stopPrice <= 0m || stopPrice >= market.Ask)
        {
            return new(
                TradingAction.Wait,
                "Insufficient target room",
                latest.Timestamp,
                market.Bid,
                StrategyVersion: breakoutVersion
            );
        }

        if (
            latest.Close > previous.Max(bar => bar.High)
            && ema20 > ema50
            && latest.Volume >= medianVolume * legacyRules.VolumeRatio
        )
        {
            return new(
                TradingAction.Buy,
                "Breakout with EMA and volume",
                latest.Timestamp,
                market.Ask,
                StopPrice: stopPrice,
                StopCandle: recent.First(bar => bar.Low == structuralLow).Timestamp,
                StrategyVersion: breakoutVersion
            );
        }

        var pullbackLimit = RoundDown(Math.Min(market.Ask, ema20) * (1m - 0.001m), market.PriceStep, market.PriceScale);
        var nearSupport = latest.Low <= structuralLow * 1.01m && latest.Close > latest.Open && latest.Close > ema20;
        var trendPullback =
            ema20 > ema50 && latest.Close > ema50 && nearSupport && latest.Volume >= medianVolume * minimumVolumeRatio;
        var atr = Atr(bars, recentBars);
        var rangeRebound =
            Math.Abs(ema20 - ema50) <= atr * 0.5m
            && previous.Max(bar => bar.High) - pullbackLimit > costHurdle
            && latest.Low <= structuralLow * 1.005m
            && latest.Close > latest.Open
            && latest.Close > structuralLow * 1.01m;
        if ((trendPullback || rangeRebound) && pullbackLimit > stopPrice && pullbackLimit < market.Ask)
        {
            var reason = trendPullback ? "Trend pullback at support" : "Range rebound at support";
            var version = trendPullback ? pullbackVersion : rangeVersion;
            return new(
                TradingAction.Buy,
                reason,
                latest.Timestamp,
                market.Ask,
                EntryLimit: pullbackLimit,
                StopPrice: stopPrice,
                StopCandle: recent.First(bar => bar.Low == structuralLow).Timestamp,
                StrategyVersion: version
            );
        }

        return new(
            TradingAction.Wait,
            "No adaptive entry signal",
            latest.Timestamp,
            market.Bid,
            StrategyVersion: breakoutVersion
        );
    }

    public static decimal? OpportunityScore(TradingRuleSet rules, TradingBook market, TradingDecision decision)
    {
        if (decision.Action != TradingAction.Buy || decision.StopPrice is not { } stop || market.Bars.Count < 21)
        {
            return null;
        }

        var limit = decision.EntryLimit ?? market.Ask * (1 + rules.SlippageBps / 10000m);
        if (limit <= stop || market.Bid <= 0)
        {
            return null;
        }

        var reward = decimal.Max(
            Atr(market.Bars.OrderBy(bar => bar.Timestamp).ToArray(), recentBars) * 2,
            limit * rules.TakeProfitBps / 10000m
        );
        var spread = (market.Ask - market.Bid) / market.Bid;
        var costs = limit * ((rules.FeeBps * 2 + rules.SlippageBps) / 10000m + spread);
        return decimal.Max(0, reward - costs) / (limit - stop);
    }

    private static decimal Ema(IReadOnlyList<TradingBar> bars, int period)
    {
        var value = bars.Take(period).Average(bar => bar.Close);
        foreach (var bar in bars.Skip(period))
        {
            value = bar.Close * 2m / (period + 1) + value * (period - 1) / (period + 1);
        }

        return value;
    }

    private static decimal Atr(TradingBar[] bars, int period)
    {
        var start = Math.Max(1, bars.Length - period);
        return bars.Skip(start)
            .Select(
                (bar, index) =>
                    Math.Max(
                        bar.High - bar.Low,
                        Math.Max(
                            Math.Abs(bar.High - bars[start + index - 1].Close),
                            Math.Abs(bar.Low - bars[start + index - 1].Close)
                        )
                    )
            )
            .Average();
    }

    private static decimal RoundDown(decimal value, decimal step, int scale)
    {
        if (step <= 0m)
        {
            return decimal.Round(value, scale, MidpointRounding.ToZero);
        }

        return decimal.Round(Math.Floor(value / step) * step, scale, MidpointRounding.ToZero);
    }
}
