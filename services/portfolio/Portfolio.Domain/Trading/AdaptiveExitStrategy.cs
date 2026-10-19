namespace Quantara.Portfolio.Domain.Trading;

public static class AdaptiveExitStrategy
{
    public static TradingExitPlan Create(
        TradingRuleSet rules,
        TradingBook book,
        TradingDecision decision,
        Guid intentId,
        string rulesHash,
        DateTimeOffset now
    )
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(decision);
        rules.Validate();
        if (
            rules.ExitPolicy is not { } policy
            || decision.Action != TradingAction.Buy
            || decision.StopPrice is not > 0
            || intentId == Guid.Empty
            || rulesHash.Length != 64
            || !TryContext(book, now, out var fast, out var slow, out var atr)
            || atr <= 0
        )
        {
            throw new ArgumentException("A verified entry and completed native candles are required for an exit plan.");
        }

        var regime = Regime(fast, slow, book.Bars[^1].Close, atr);
        var limit = decision.EntryLimit ?? book.Ask * (1 + rules.SlippageBps / 10000m);
        if (limit <= decision.StopPrice)
        {
            throw new ArgumentException("Entry must be above its protective stop.");
        }

        var plan = new TradingExitPlan
        {
            EntryIntentId = intentId,
            RulesHash = rulesHash,
            PreparedAt = now,
            EvidenceAt = book.ObservedAt,
            EntryCandle = book.Bars[^1].Timestamp,
            Policy = policy,
            FeeBps = rules.FeeBps,
            SlippageBps = rules.SlippageBps,
            MaximumDataAgeSeconds = rules.MaxDataAgeSeconds,
            Regime = regime,
            TimeframeSeconds = book.TimeframeSeconds,
            EnteredAt = now,
            EntryAtr = atr,
            CurrentAtr = atr,
            EffectiveStop = decision.StopPrice.Value,
            TargetPrice = limit + Math.Max(atr * 2, limit * rules.TakeProfitBps / 10000m),
            NextReviewAt = now.AddSeconds(ReviewSeconds(policy, regime, book.TimeframeSeconds)),
            HardExitAt = now.AddMinutes(MaximumMinutes(policy, regime)),
        };
        plan.Validate();
        return plan;
    }

    public static TradingExitPlan Anchor(TradingExitPlan plan, DateTimeOffset firstFillAt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();
        return plan.Anchored
            ? plan
            : plan with
            {
                Anchored = true,
                EnteredAt = firstFillAt,
                NextReviewAt = firstFillAt.AddSeconds(ReviewSeconds(plan.Policy, plan.Regime, plan.TimeframeSeconds)),
                HardExitAt = firstFillAt.AddMinutes(MaximumMinutes(plan.Policy, plan.Regime)),
                LastReason = "Position monitored under its entry plan",
            };
    }

    public static TradingExitAssessment Advance(
        TradingRuleSet rules,
        TradingBook book,
        TradingHolding holding,
        DateTimeOffset now
    )
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(holding);
        var original = holding.ExitPlan ?? throw new ArgumentException("The position has no installed exit plan.");
        var plan = original;
        plan.Validate();
        if (
            !plan.Anchored
            || plan.Version != "adaptive-exit-v1"
            || holding.Symbol != book.Symbol
            || holding.Quantity <= 0
            || holding.CostThb <= 0
            || book.Bid <= 0
            || !(book.SellEnabled ?? book.Enabled)
            || book.ObservedAt > now
            || now - book.ObservedAt > TimeSpan.FromSeconds(plan.MaximumDataAgeSeconds)
        )
        {
            return new(original, TradingAction.Wait, "Exit quote or position is unavailable");
        }

        if (plan.ExitReason is { } pending)
        {
            return new(plan, TradingAction.Sell, pending);
        }

        if (book.Bid <= plan.EffectiveStop)
        {
            return Exit(original, plan, plan.TrailingActive ? "Adaptive trailing protection" : "Protective stop");
        }

        if (now >= plan.HardExitAt)
        {
            return Exit(original, plan, "Maximum holding deadline");
        }

        decimal fast = 0,
            slow = 0,
            atr = plan.CurrentAtr;
        var contextReady =
            book.TimeframeSeconds == plan.TimeframeSeconds && TryContext(book, now, out fast, out slow, out atr);
        var reviewed = false;
        if (contextReady)
        {
            plan = plan with { CurrentAtr = atr };
            var latest = book.Bars[^1];
            if (now >= plan.NextReviewAt && latest.Timestamp > plan.LastReviewedCandle)
            {
                reviewed = true;
                var observedRegime = Regime(fast, slow, latest.Close, atr);
                var evidence =
                    observedRegime != TradingExitRegime.Unknown && observedRegime != plan.Regime
                        ? (observedRegime == plan.PendingRegime ? plan.RegimeEvidenceCount + 1 : 1)
                        : 0;
                if (evidence >= 2)
                {
                    var deadline = plan.EnteredAt.AddMinutes(MaximumMinutes(plan.Policy, observedRegime));
                    plan = plan with
                    {
                        Regime = observedRegime,
                        HardExitAt = deadline < plan.HardExitAt ? deadline : plan.HardExitAt,
                    };
                    evidence = 0;
                }

                var next = now.AddSeconds(ReviewSeconds(plan.Policy, plan.Regime, plan.TimeframeSeconds));
                var progressed = latest.Close - holding.AverageCost >= atr * plan.Policy.MinimumProgressAtr;
                plan = plan with
                {
                    LastReviewedCandle = latest.Timestamp,
                    ReviewCount = plan.ReviewCount + 1,
                    NoProgressCount = progressed ? 0 : plan.NoProgressCount + 1,
                    PendingRegime = evidence == 0 ? TradingExitRegime.Unknown : observedRegime,
                    RegimeEvidenceCount = evidence,
                    NextReviewAt = next < plan.HardExitAt ? next : plan.HardExitAt,
                };
                if (now >= plan.HardExitAt)
                {
                    return Exit(original, plan, "Maximum holding deadline after confirmed regime change");
                }

                var minimum =
                    plan.Regime == TradingExitRegime.Trend
                        ? plan.Policy.TrendMinimumMinutes
                        : plan.Policy.RangeMinimumMinutes;
                if (now - plan.EnteredAt >= TimeSpan.FromMinutes(minimum))
                {
                    if (latest.Close < slow && fast < slow)
                    {
                        return Exit(original, plan, "Trend invalidated on completed candle");
                    }

                    if (
                        plan.ReviewCount >= plan.Policy.MaximumReviews
                        || plan.NoProgressCount >= plan.Policy.NoProgressReviews
                    )
                    {
                        return Exit(original, plan, "No progress within the bounded review allowance");
                    }
                }
            }
        }
        else
        {
            atr = plan.CurrentAtr;
        }

        // CostThb already includes the entry fee. Only estimated exit costs are subtracted here.
        var exitFactor = 1 - (plan.FeeBps + plan.SlippageBps) / 10000m;
        var peak = Math.Max(holding.PeakBid, book.Bid);
        if (
            !plan.TrailingActive
            && peak * exitFactor * holding.Quantity > holding.CostThb
            && peak - holding.AverageCost >= plan.EntryAtr * plan.Policy.ActivationAtr
        )
        {
            plan = plan with { TrailingActive = true };
        }

        if (plan.TrailingActive && atr > 0)
        {
            var multiple =
                plan.Regime == TradingExitRegime.Trend ? plan.Policy.TrendTrailingAtr : plan.Policy.RangeTrailingAtr;
            plan = plan with { EffectiveStop = Math.Max(plan.EffectiveStop, peak - atr * multiple) };
            if (book.Bid <= plan.EffectiveStop)
            {
                return Exit(original, plan, "Adaptive trailing protection");
            }
        }

        if (plan.Regime == TradingExitRegime.Range && book.Bid >= plan.TargetPrice)
        {
            return Exit(original, plan, "Range target reached");
        }

        if (!contextReady)
        {
            return Hold(original, plan, "Review evidence unavailable; original protection and deadline retained");
        }

        if (!reviewed)
        {
            return Hold(original, plan, plan.LastReason);
        }

        return Hold(
            original,
            plan,
            plan.NoProgressCount == 0
                ? "Setup intact; position retained after review"
                : "Monitoring progress within the entry plan"
        );
    }

    private static TradingExitAssessment Exit(TradingExitPlan original, TradingExitPlan plan, string reason) =>
        new(
            plan with
            {
                Revision = original.Revision + 1,
                LastReason = reason,
                ExitReason = reason,
            },
            TradingAction.Sell,
            reason
        );

    private static TradingExitAssessment Hold(TradingExitPlan original, TradingExitPlan plan, string reason)
    {
        plan = plan with { LastReason = reason };
        return new(
            plan == original ? original : plan with { Revision = original.Revision + 1 },
            TradingAction.Wait,
            reason
        );
    }

    private static int ReviewSeconds(AdaptiveExitPolicy policy, TradingExitRegime regime, int timeframe) =>
        Math.Min(
            (regime == TradingExitRegime.Trend ? policy.TrendReviewBars : policy.RangeReviewBars) * timeframe,
            MaximumMinutes(policy, regime) * 60
        );

    private static int MaximumMinutes(AdaptiveExitPolicy policy, TradingExitRegime regime) =>
        regime == TradingExitRegime.Trend ? policy.TrendMaximumMinutes : policy.RangeMaximumMinutes;

    private static TradingExitRegime Regime(decimal fast, decimal slow, decimal close, decimal atr) =>
        fast > slow + atr * 0.5m && close >= slow ? TradingExitRegime.Trend
        : Math.Abs(fast - slow) <= atr * 0.5m ? TradingExitRegime.Range
        : TradingExitRegime.Unknown;

    private static bool TryContext(
        TradingBook book,
        DateTimeOffset now,
        out decimal fast,
        out decimal slow,
        out decimal atr
    )
    {
        fast = slow = atr = 0;
        var seconds = book.TimeframeSeconds;
        if (!TradingTimeframes.Supported(seconds) || book.Bars.Count < 51)
        {
            return false;
        }

        var bars = book.Bars;
        if (bars[^1].Timestamp != TradingTimeframes.LastCompletedOpen(now, seconds))
        {
            return false;
        }

        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            if (
                bar.Timestamp % seconds != 0
                || index > 0 && bar.Timestamp - bars[index - 1].Timestamp != seconds
                || bar.Open <= 0
                || bar.Close <= 0
                || bar.Low <= 0
                || bar.High < Math.Max(bar.Open, bar.Close)
                || bar.Low > Math.Min(bar.Open, bar.Close)
                || bar.Volume < 0
            )
            {
                return false;
            }
        }

        fast = bars.Take(20).Average(bar => bar.Close);
        slow = bars.Take(50).Average(bar => bar.Close);
        for (var index = 20; index < bars.Count; index++)
        {
            fast += (bars[index].Close - fast) * 2m / 21m;
            if (index >= 50)
            {
                slow += (bars[index].Close - slow) * 2m / 51m;
            }
        }

        atr = Enumerable
            .Range(bars.Count - 20, 20)
            .Average(index =>
                Math.Max(
                    bars[index].High - bars[index].Low,
                    Math.Max(
                        Math.Abs(bars[index].High - bars[index - 1].Close),
                        Math.Abs(bars[index].Low - bars[index - 1].Close)
                    )
                )
            );
        return atr > 0;
    }
}
