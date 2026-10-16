namespace Quantara.Portfolio.Domain.Trading;

public static class TradingStrategy
{
    public static TradingDecision Evaluate(
        TradingRuleSet rules,
        TradingBook market,
        TradingHolding? holding,
        DateTimeOffset now
    )
    {
        rules.Validate();
        var candle = market.Bars.Count == 0 ? 0L : market.Bars[^1].Timestamp;
        if (
            !market.Enabled
            || market.Bid <= 0m
            || market.Ask <= 0m
            || market.Ask < market.Bid
            || market.Symbol is null
            || !rules.Symbols.Contains(market.Symbol, StringComparer.Ordinal)
        )
        {
            return new(TradingAction.Wait, "Invalid quote or symbol", candle, 0m);
        }

        var age = now - market.ObservedAt;
        var spread = (market.Ask - market.Bid) / market.Bid * 10000m;
        if (age < TimeSpan.Zero || age.TotalSeconds > rules.MaxDataAgeSeconds || spread > rules.MaxSpreadBps)
        {
            return new(TradingAction.Wait, "Stale or wide market", candle, market.Bid);
        }

        if (holding is not null)
        {
            return EvaluateExit(rules, market, holding, now);
        }

        if (market.Bars.Count < 51)
        {
            return new(TradingAction.Wait, "Insufficient completed candles", candle, market.Bid);
        }

        var bars = market.Bars.OrderBy(bar => bar.Timestamp).ToArray();
        var contiguousStart = 0;
        for (var index = 1; index < bars.Length; index++)
        {
            if (bars[index].Timestamp - bars[index - 1].Timestamp != 300)
            {
                contiguousStart = index;
            }
        }

        bars = bars[contiguousStart..];
        if (bars.Length < 51)
        {
            return new(TradingAction.Wait, "Insufficient contiguous completed candles", candle, market.Bid);
        }

        for (var i = 0; i < bars.Length; i++)
        {
            if (
                (i > 0 && bars[i].Timestamp - bars[i - 1].Timestamp != 300)
                || bars[i].High < Math.Max(bars[i].Open, bars[i].Close)
                || bars[i].Low > Math.Min(bars[i].Open, bars[i].Close)
                || bars[i].Low <= 0m
                || bars[i].High < bars[i].Low
                || bars[i].Volume < 0m
                || bars[i].Timestamp % 300 != 0
            )
            {
                return new(TradingAction.Wait, "Malformed or gapped candles", candle, market.Bid);
            }
        }

        var latest = bars[^1];
        if (
            now - DateTimeOffset.FromUnixTimeSeconds(latest.Timestamp) < TimeSpan.FromMinutes(5)
            || now - DateTimeOffset.FromUnixTimeSeconds(latest.Timestamp) > TimeSpan.FromMinutes(10)
        )
        {
            return new(TradingAction.Wait, "Incomplete or stale candle", candle, market.Bid);
        }

        var previous = bars[^21..^1];
        var sortedVolumes = previous.Select(bar => bar.Volume).OrderBy(x => x).ToArray();
        var median = (sortedVolumes[9] + sortedVolumes[10]) / 2m;
        var ema20 = Ema(bars, 20);
        var ema50 = Ema(bars, 50);
        if (
            median > 0m
            && latest.Close > previous.Max(bar => bar.High)
            && ema20 > ema50
            && latest.Volume >= median * rules.VolumeRatio
        )
        {
            return new(TradingAction.Buy, "Breakout with EMA and volume", latest.Timestamp, market.Ask);
        }

        return new(TradingAction.Wait, "No entry signal", latest.Timestamp, market.Bid);
    }

    public static TradingDecision EvaluateExit(
        TradingRuleSet rules,
        TradingBook market,
        TradingHolding holding,
        DateTimeOffset now
    )
    {
        rules.Validate();
        var candle = market.Bars.Count == 0 ? 0 : market.Bars[^1].Timestamp;
        if (
            holding.Symbol != market.Symbol
            || holding.Quantity <= 0
            || holding.CostThb <= 0
            || market.Bid <= 0
            || !(market.SellEnabled ?? market.Enabled)
            || market.ObservedAt > now
            || now - market.ObservedAt > TimeSpan.FromSeconds(rules.MaxDataAgeSeconds)
        )
        {
            return new(TradingAction.Wait, "Invalid holding or exit quote", candle, market.Bid);
        }

        var average = holding.AverageCost;
        if (market.Bid <= average * (1 - rules.StopLossBps / 10000m))
        {
            return new(TradingAction.Sell, "Stop loss", candle, market.Bid);
        }

        if (market.Bid >= average * (1 + rules.TakeProfitBps / 10000m))
        {
            return new(TradingAction.Sell, "Take profit", candle, market.Bid);
        }

        if (holding.PeakBid > average && market.Bid <= holding.PeakBid * (1 - rules.TrailingStopBps / 10000m))
        {
            return new(TradingAction.Sell, "Trailing stop", candle, market.Bid);
        }

        return now - holding.OpenedAt >= TimeSpan.FromMinutes(rules.TimeExitMinutes)
            ? new(TradingAction.Sell, "Time exit", candle, market.Bid)
            : new(TradingAction.Wait, "Position monitored", candle, market.Bid);
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
}
