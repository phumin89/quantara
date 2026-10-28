namespace Quantara.Portfolio.Domain.Trading;

public static class TradingReplayEvaluator
{
    public const string Protocol = "ohlc-scenario-v3";

    public static bool HasPrecision(TradingBook? book) =>
        book
            is {
                PriceStep: > 0,
                MinimumNotional: > 0,
                PriceScale: >= 0 and <= 18,
                BaseScale: >= 0 and <= 18,
                QuoteScale: >= 0 and <= 18
            }
        && decimal.Round(book.PriceStep, book.PriceScale, MidpointRounding.ToZero) == book.PriceStep;

    public static IReadOnlyList<TradingEvaluationMetric> Evaluate(
        TradingRuleSet rules,
        string symbol,
        IReadOnlyList<TradingBar> source,
        int interval,
        decimal budget,
        TradingBook? market
    )
    {
        rules.Validate();
        if (
            rules.Version != "adaptive-intraday-v2"
            || !TradingTimeframes.Supported(interval)
            || budget is < 10 or > 1000000
        )
        {
            throw new ArgumentException(
                "Evaluation requires a versioned native timeframe and bounded reference budget."
            );
        }

        var bars = source.OrderBy(bar => bar.Timestamp).ToArray();
        if (bars.Length > 10000 || bars.Select(bar => bar.Timestamp).Distinct().Count() != bars.Length)
        {
            throw new ArgumentException("Historical evaluation requires bounded unique candles.");
        }

        var split = bars.Length * 7 / 10;
        var result = new List<TradingEvaluationMetric>();
        foreach (var strategy in new[] { "breakout", "pullback", "range" })
        {
            if (!HasPrecision(market) || market is null || market.Symbol != symbol)
            {
                foreach (var segment in new[] { "training", "validation" })
                {
                    result.Add(new(symbol, interval, segment, strategy, 0, 0, 0, null, 0m, null, 0m, true));
                }

                continue;
            }

            result.Add(Replay(rules, market, bars, interval, budget, strategy, "training", 50, split));
            result.Add(
                Replay(rules, market, bars, interval, budget, strategy, "validation", Math.Max(50, split), bars.Length)
            );
        }

        return result;
    }

    private static TradingEvaluationMetric Replay(
        TradingRuleSet rules,
        TradingBook market,
        TradingBar[] bars,
        int interval,
        decimal budget,
        string strategy,
        string segment,
        int start,
        int end
    )
    {
        TradingShadow? sample = null;
        decimal? sellLimit = null;
        DateTimeOffset? sellExpiry = null;
        var samples = 0;
        var trades = 0;
        var unknown = 0;
        var net = 0m;
        var fees = 0m;
        var peak = 0m;
        var drawdown = 0m;
        var feeRate = rules.FeeBps / 10000m;
        var missingData = false;
        for (var index = start; index < end; index++)
        {
            var bar = bars[index];
            var opened = DateTimeOffset.FromUnixTimeSeconds(bar.Timestamp);
            var closed = opened.AddSeconds(interval);
            if (!ValidWindow(bars, index, interval))
            {
                missingData = true;
                if (sample is not null)
                {
                    unknown++;
                    sample = null;
                    sellLimit = null;
                }

                continue;
            }

            samples++;
            if (sample is { State: TradingShadowState.Waiting })
            {
                if (opened >= sample.ExpiresAt)
                {
                    sample = null;
                    continue;
                }

                if (closed > sample.ExpiresAt)
                {
                    unknown++;
                    sample = null;
                    continue;
                }

                if (bar.Low * (1 + rules.MaxSpreadBps / 10000m) < sample.LimitPrice && bar.Volume > 0)
                {
                    if (bar.Low <= sample.StopPrice || bar.High >= sample.TargetPrice)
                    {
                        unknown++;
                        sample = null;
                        continue;
                    }

                    var reservedFee = decimal.Ceiling(budget * feeRate * 100) / 100;
                    var amount = decimal.Round(budget - reservedFee, market.QuoteScale, MidpointRounding.ToZero);
                    var quantity = decimal.Round(amount / sample.LimitPrice, market.BaseScale, MidpointRounding.ToZero);
                    if (quantity <= 0 || quantity * sample.LimitPrice < market.MinimumNotional)
                    {
                        unknown++;
                        sample = null;
                        continue;
                    }

                    var fee = decimal.Ceiling(quantity * sample.LimitPrice * feeRate * 100) / 100;
                    sample = sample with
                    {
                        State = TradingShadowState.Open,
                        Quantity = quantity,
                        EntryCostThb = quantity * sample.LimitPrice + fee,
                        EntryAt = closed,
                        PeakBid = sample.LimitPrice,
                    };
                    fees += fee;
                    var markedEntry = net + quantity * bar.Close * (1 - feeRate) - sample.EntryCostThb;
                    peak = Math.Max(peak, markedEntry);
                    drawdown = Math.Max(drawdown, peak - markedEntry);
                }

                continue;
            }

            if (sample is { State: TradingShadowState.Open })
            {
                var markedNet = net + sample.Quantity * bar.Close * (1 - feeRate) - sample.EntryCostThb;
                peak = Math.Max(peak, markedNet);
                drawdown = Math.Max(drawdown, peak - markedNet);
                if (sellLimit is { } limit)
                {
                    if (closed > sellExpiry)
                    {
                        unknown++;
                        sample = null;
                        sellLimit = null;
                        continue;
                    }

                    if (bar.High > limit && bar.Volume > 0)
                    {
                        var gross = sample.Quantity * limit;
                        var fee = decimal.Ceiling(gross * feeRate * 100) / 100;
                        fees += fee;
                        net += gross - fee - sample.EntryCostThb;
                        peak = Math.Max(peak, net);
                        drawdown = Math.Max(drawdown, peak - net);
                        trades++;
                        sample = null;
                        sellLimit = null;
                    }

                    continue;
                }

                var trailing =
                    sample.PeakBid > sample.EntryCostThb / sample.Quantity
                    && bar.Close <= sample.PeakBid - sample.TrailingDistance;
                var exit =
                    bar.Close <= sample.StopPrice
                    || bar.Close >= sample.TargetPrice
                    || trailing
                    || closed - sample.EntryAt >= TimeSpan.FromMinutes(sample.TimeExitMinutes);
                if (
                    !exit
                    && (
                        bar.Low <= sample.StopPrice
                        || bar.High >= sample.TargetPrice
                        || sample.PeakBid > sample.LimitPrice && bar.Low <= sample.PeakBid - sample.TrailingDistance
                    )
                )
                {
                    unknown++;
                    sample = null;
                    continue;
                }

                if (exit)
                {
                    sellLimit = decimal.Round(
                        decimal.Ceiling(bar.Close * (1 - rules.SlippageBps / 10000m) / market.PriceStep)
                            * market.PriceStep,
                        market.PriceScale,
                        MidpointRounding.ToZero
                    );
                    if (sellLimit <= 0m)
                    {
                        unknown++;
                        sample = null;
                        sellLimit = null;
                        continue;
                    }

                    sellExpiry = closed.AddMinutes(rules.OrderLifetimeMinutes);
                }
                else
                {
                    sample = sample with { PeakBid = Math.Max(sample.PeakBid, bar.Close) };
                }

                continue;
            }

            var first = index;
            while (first > 0 && index - first < 119 && bars[first].Timestamp - bars[first - 1].Timestamp == interval)
            {
                first--;
            }

            var book = new TradingBook(
                market.Symbol,
                closed,
                bar.Close,
                bar.Close * (1 + rules.MaxSpreadBps / 10000m),
                bar.Volume,
                bar.Volume,
                bars[first..(index + 1)],
                MinimumNotional: market.MinimumNotional,
                PriceStep: market.PriceStep,
                PriceScale: market.PriceScale,
                BaseScale: market.BaseScale,
                QuoteScale: market.QuoteScale,
                TimeframeSeconds: interval
            );
            var signal = AdaptiveTradingStrategy.Evaluate(rules, book, null, closed);
            if (
                signal.Action == TradingAction.Buy
                && signal.StrategyVersion?.StartsWith(strategy, StringComparison.Ordinal) == true
            )
            {
                sample = TradingShadowProtocol.Capture("scenario", rules, book, signal, closed) with
                {
                    BudgetThb = budget,
                };
            }
        }

        if (sample is not null)
        {
            unknown++;
        }

        var complete = samples > 0 && unknown == 0 && !missingData;
        return new(
            market.Symbol,
            interval,
            segment,
            strategy,
            samples,
            trades,
            unknown,
            complete ? net : null,
            fees,
            complete ? drawdown : null,
            0m,
            samples < 30 || trades < 20
        );
    }

    private static bool ValidWindow(TradingBar[] bars, int end, int interval)
    {
        for (var index = Math.Max(0, end - 50); index <= end; index++)
        {
            var bar = bars[index];
            if (
                bar.Timestamp % interval != 0
                || bar.Open <= 0
                || bar.Close <= 0
                || bar.Low <= 0
                || bar.Volume < 0
                || bar.High < Math.Max(bar.Open, bar.Close)
                || bar.Low > Math.Min(bar.Open, bar.Close)
                || index > end - 50 && bar.Timestamp - bars[index - 1].Timestamp != interval
            )
            {
                return false;
            }
        }

        return end >= 50;
    }
}
