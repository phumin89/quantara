namespace Quantara.Portfolio.Domain.Trading;

public static class TradingShadowProtocol
{
    public const string Version = "observed-book-v1";

    public static TradingShadow Capture(
        string key,
        TradingRuleSet rules,
        TradingBook book,
        TradingDecision signal,
        DateTimeOffset now
    )
    {
        var bars = book.Bars.OrderBy(bar => bar.Timestamp).TakeLast(21).ToArray();
        var atr = bars.Zip(bars.Skip(1))
            .Average(pair =>
                decimal.Max(
                    pair.Second.High - pair.Second.Low,
                    decimal.Max(
                        decimal.Abs(pair.Second.High - pair.First.Close),
                        decimal.Abs(pair.Second.Low - pair.First.Close)
                    )
                )
            );
        var rawLimit = signal.EntryLimit ?? book.Ask * (1 + rules.SlippageBps / 10000m);
        var limit = decimal.Floor(rawLimit / book.PriceStep) * book.PriceStep;
        return new(
            key,
            book.Symbol,
            signal.StrategyVersion ?? rules.Version,
            now,
            now.AddMinutes(rules.OrderLifetimeMinutes),
            rules.TimeExitMinutes,
            rules.MaxDataAgeSeconds,
            limit,
            signal.StopPrice ?? throw new ArgumentException("A structural stop is required."),
            limit + decimal.Max(atr * 2, limit * rules.TakeProfitBps / 10000m),
            decimal.Max(atr, limit * rules.TrailingStopBps / 10000m),
            book.MinimumNotional * 10,
            rules.FeeBps,
            book.BaseScale
        );
    }

    public static TradingShadow Advance(TradingShadow sample, TradingBook? book, DateTimeOffset now)
    {
        if (sample.State is not (TradingShadowState.Waiting or TradingShadowState.Open))
        {
            return sample;
        }

        if (
            sample.BudgetThb <= 0
            || sample.BaseScale is < 0 or > 18
            || sample.StopPrice <= 0
            || sample.LimitPrice <= sample.StopPrice
            || sample.TargetPrice <= sample.LimitPrice
            || sample.FeeBps is < 0 or > 100
            || sample.State == TradingShadowState.Open
                && (sample.Quantity <= 0 || sample.EntryCostThb <= 0 || sample.EntryAt is null)
        )
        {
            return sample with { State = TradingShadowState.Unknown, ClosedAt = now };
        }

        if (sample.State == TradingShadowState.Waiting && now >= sample.ExpiresAt)
        {
            return sample with { State = TradingShadowState.Expired, ClosedAt = now };
        }

        var fresh =
            book is not null
            && book.Symbol == sample.Symbol
            && book.Bid > 0
            && book.Ask >= book.Bid
            && book.ObservedAt > (sample.EntryAt ?? sample.CreatedAt)
            && book.ObservedAt <= now
            && now - book.ObservedAt <= TimeSpan.FromSeconds(sample.MaximumDataAgeSeconds);
        if (!fresh || book is null)
        {
            return
                sample.EntryAt is { } entry
                && now - entry
                    > TimeSpan
                        .FromMinutes(sample.TimeExitMinutes)
                        .Add(TimeSpan.FromSeconds(sample.MaximumDataAgeSeconds))
                ? sample with
                {
                    State = TradingShadowState.Unknown,
                    ClosedAt = now,
                }
                : sample;
        }

        var feeRate = sample.FeeBps / 10000m;
        if (sample.State == TradingShadowState.Waiting)
        {
            if (!book.Enabled || book.Ask > sample.LimitPrice || book.Bid <= sample.StopPrice)
            {
                return sample;
            }

            var fee = decimal.Ceiling(sample.BudgetThb * feeRate * 100) / 100;
            var multiplier = (decimal)Math.Pow(10, sample.BaseScale);
            var quantity = decimal.Floor((sample.BudgetThb - fee) / book.Ask * multiplier) / multiplier;
            if (quantity <= 0 || quantity > book.AskQuantity)
            {
                return sample;
            }

            return sample with
            {
                State = TradingShadowState.Open,
                EntryAt = book.ObservedAt,
                Quantity = quantity,
                EntryCostThb = quantity * book.Ask + fee,
                PeakBid = book.Ask,
            };
        }

        sample = sample with { PeakBid = decimal.Max(sample.PeakBid, book.Bid) };
        var trailing =
            sample.PeakBid > sample.EntryCostThb / sample.Quantity
            && book.Bid <= sample.PeakBid - sample.TrailingDistance;
        if (
            !trailing
            && book.Bid > sample.StopPrice
            && book.Bid < sample.TargetPrice
            && sample.EntryAt is { } opened
            && now - opened < TimeSpan.FromMinutes(sample.TimeExitMinutes)
        )
        {
            return sample;
        }

        if (!(book.SellEnabled ?? book.Enabled) || book.BidQuantity < sample.Quantity)
        {
            return sample with { State = TradingShadowState.Unknown, ClosedAt = now };
        }

        var proceeds = sample.Quantity * book.Bid;
        var exitFee = decimal.Ceiling(proceeds * feeRate * 100) / 100;
        return sample with
        {
            State = TradingShadowState.Closed,
            ClosedAt = now,
            NetThb = proceeds - exitFee - sample.EntryCostThb,
        };
    }
}
