using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingExitStudy
{
    public static TradingState Advance(TradingState state, IReadOnlyList<TradingBook> books, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(books);
        var seeded = state
            .Paper.Orders.Concat(state.Live.Orders)
            .Where(order => order.Side == TradeSide.Buy && order.ExitStudyPlan is not null)
            .ToArray();
        if (state.ExitStudies.Count == 0 && seeded.Length == 0)
        {
            return state;
        }

        var next = state.ExitStudies.ToDictionary(
            sample => (sample.SourceMode, sample.EntryIntentId),
            sample => sample
        );
        var processed = 0;
        foreach (var (mode, ledger) in new[] { ("paper", state.Paper), ("live", state.Live) })
        {
            var entries = ledger
                .Fills.Where(fill => fill.Side == TradeSide.Buy && fill.OccurredAt <= now)
                .GroupBy(fill => fill.IntentId)
                .ToDictionary(group => group.Key, group => group.OrderBy(fill => fill.OccurredAt).ToArray());
            foreach (
                var order in ledger.Orders.Where(order =>
                    order.Side == TradeSide.Buy && order.ExitStudyPlan is not null
                )
            )
            {
                var fills = entries.GetValueOrDefault(order.Id) ?? [];
                if (fills.Length == 0)
                {
                    continue;
                }

                var key = (mode, order.Id);
                var prior = next.GetValueOrDefault(key);
                if (prior is { Status: "hypothetical" or "unknown" or "baseline" })
                {
                    if (
                        prior.Quantity != fills.Sum(TradingLedgerAccounting.CreditedQuantity)
                        || prior.EntryCostThb != fills.Sum(fill => fill.GrossThb + fill.FeeThb)
                    )
                    {
                        next[key] = prior with
                        {
                            Status = "unknown",
                            HypotheticalNetThb = null,
                            HypotheticalExitAt = null,
                            BaselineNetThb = null,
                            BaselineExitAt = null,
                            Reason = "Entry quantity or cost evidence changed after the hypothetical exit.",
                        };
                        continue;
                    }

                    if (prior.BaselineNetThb is null)
                    {
                        next[key] = ReconcileBaseline(prior, ledger, fills, order, now);
                    }

                    continue;
                }

                if (processed++ >= 32)
                {
                    continue;
                }

                var first = fills[0];
                var quantity = fills.Sum(TradingLedgerAccounting.CreditedQuantity);
                var cost = fills.Sum(fill => fill.GrossThb + fill.FeeThb);
                var book =
                    books.FirstOrDefault(item =>
                        item.Symbol == order.Symbol && item.TimeframeSeconds == order.ExitStudyPlan?.TimeframeSeconds
                    ) ?? books.FirstOrDefault(item => item.Symbol == order.Symbol);
                var existing = prior;
                var seed =
                    order.ExitStudyPlan
                    ?? throw new InvalidOperationException("Prospective exit study seed is missing.");
                TradingExitPlan plan;
                try
                {
                    plan = AdaptiveExitStrategy.Anchor(seed, first.OccurredAt);
                }
                catch (ArgumentException)
                {
                    next[key] = new TradingExitStudySample
                    {
                        SourceMode = mode,
                        SourceRulesHash = order.RulesHash,
                        EntryIntentId = order.Id,
                        Symbol = order.Symbol,
                        CreatedAt = first.OccurredAt,
                        Quantity = quantity,
                        EntryCostThb = cost,
                        Status = "unknown",
                        Reason = "The prospective seed is invalid; execution authority is unchanged.",
                    };
                    continue;
                }

                var fresh =
                    book is not null
                    && book.ObservedAt <= now
                    && now - book.ObservedAt <= TimeSpan.FromSeconds(plan.MaximumDataAgeSeconds);
                var holding = new TradingHolding(
                    order.Symbol,
                    quantity,
                    cost,
                    first.OccurredAt,
                    Math.Max(existing?.Holding?.PeakBid ?? first.Price, fresh ? book?.Bid ?? first.Price : first.Price)
                )
                {
                    ExitPlan = existing?.ExitPlan ?? plan,
                };
                if (book is null)
                {
                    next[key] = Sample(mode, order, first, holding, "active", null);
                    continue;
                }

                TradingExitAssessment assessment;
                try
                {
                    assessment = AdaptiveExitStrategy.Advance(state.Rules, book, holding, now);
                }
                catch (Exception error) when (error is ArgumentException or ArithmeticException)
                {
                    next[key] = Sample(mode, order, first, holding, "unknown", null) with
                    {
                        Reason = "Exit study evidence is invalid; execution authority is unchanged.",
                    };
                    continue;
                }

                var updated = holding with { ExitPlan = assessment.Plan };
                if (assessment.Action != TradingAction.Sell)
                {
                    next[key] = Sample(mode, order, first, updated, "active", null) with { Reason = assessment.Reason };
                    continue;
                }

                if (book.Bid <= 0m || book.BidQuantity < quantity)
                {
                    next[key] = Sample(mode, order, first, updated, "triggered", null) with
                    {
                        Reason = assessment.Reason,
                    };
                    continue;
                }

                var exitPrice = book.Bid * (1m - plan.SlippageBps / 10000m);
                var gross = exitPrice * quantity;
                var fee = decimal.Ceiling(gross * plan.FeeBps / 10000m * 100) / 100;
                next[key] = Sample(
                    mode,
                    order,
                    first,
                    updated,
                    TradingLedgerAccounting.Active(order) ? "unknown" : "hypothetical",
                    TradingLedgerAccounting.Active(order) ? null : gross - fee - cost
                ) with
                {
                    HypotheticalExitAt = TradingLedgerAccounting.Active(order) ? null : now,
                    Reason = TradingLedgerAccounting.Active(order)
                        ? "Entry order is still active; no hypothetical cancellation is assumed."
                        : assessment.Reason,
                };
            }
        }

        var samples = next
            .Values.OrderBy(sample => sample.CreatedAt)
            .ThenBy(sample => sample.SourceMode, StringComparer.Ordinal)
            .ToArray();
        return samples.SequenceEqual(state.ExitStudies) ? state : state with { ExitStudies = samples };
    }

    private static TradingExitStudySample Sample(
        string mode,
        TradingIntent order,
        TradingFill first,
        TradingHolding holding,
        string status,
        decimal? result
    ) =>
        new()
        {
            SourceMode = mode,
            SourceRulesHash = order.RulesHash,
            EntryIntentId = order.Id,
            Symbol = order.Symbol,
            CreatedAt = first.OccurredAt,
            HypotheticalNetThb = result,
            Status = status,
            Holding = holding,
            Quantity = holding.Quantity,
            EntryCostThb = holding.CostThb,
            ExitPlan = holding.ExitPlan,
            Assumptions =
                "Bid quote, available bid quantity, configured slippage and one estimated exit fee; hypothetical only.",
            Reason =
                status == "active" ? "Review evidence unavailable; original protection and deadline retained" : null,
        };

    private static TradingExitStudySample ReconcileBaseline(
        TradingExitStudySample sample,
        TradingLedger ledger,
        TradingFill[] buys,
        TradingIntent order,
        DateTimeOffset now
    )
    {
        var nextEntry = ledger
            .Orders.Where(candidate =>
                candidate.Side == TradeSide.Buy && candidate.Symbol == order.Symbol && candidate.Id != order.Id
            )
            .SelectMany(candidate =>
                ledger.Fills.Where(fill => fill.IntentId == candidate.Id && fill.Side == TradeSide.Buy)
            )
            .Where(fill => fill.OccurredAt > buys[0].OccurredAt && fill.OccurredAt <= now)
            .OrderBy(fill => fill.OccurredAt)
            .Select(fill => (DateTimeOffset?)fill.OccurredAt)
            .FirstOrDefault();
        var sells = ledger
            .Fills.Where(fill =>
                fill.Symbol == order.Symbol
                && fill.Side == TradeSide.Sell
                && fill.OccurredAt >= buys[0].OccurredAt
                && fill.OccurredAt <= now
                && (nextEntry is null || fill.OccurredAt < nextEntry.Value)
            )
            .ToArray();
        var baseline =
            sells.Length > 0
            && TradingLedgerAccounting.RemainingQuantity(buys.Concat(sells)) == 0m
            && sells.All(fill => fill.RealizedNetThb.HasValue)
                ? sells.Sum(fill => fill.RealizedNetThb.GetValueOrDefault())
                : (decimal?)null;
        return baseline is null
            ? sample
            : sample with
            {
                BaselineNetThb = baseline,
                BaselineExitAt = sells.Max(fill => fill.OccurredAt),
                Status = "baseline",
            };
    }
}
