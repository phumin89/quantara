using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal static class TradingLedgerValuation
{
    internal static TradingLedger Mark(
        TradingLedger ledger,
        IReadOnlyList<TradingBook> books,
        TradingRuleSet rules,
        DateTimeOffset now,
        bool force = false
    )
    {
        var positions = ledger
            .Positions.Select(position =>
            {
                var book = books.SingleOrDefault(book => book.Symbol == position.Symbol);
                if (book is null || !TradingExecutionRules.Fresh(book, rules, now, false))
                {
                    return position;
                }

                var marked = position with { PeakBid = Math.Max(position.PeakBid, book.Bid) };
                if (rules.ExitPolicy is not null && marked.ExitPlan is not null && marked.Quantity > 0)
                {
                    if (marked.ExitPlan.RulesHash != TradingRuleHash.Compute(rules))
                    {
                        throw new InvalidDataException("The owned exit plan does not match its authorized policy.");
                    }

                    var assessment = AdaptiveExitStrategy.Advance(rules, book, marked, now);
                    marked = marked with { ExitPlan = assessment.Plan, StopPrice = assessment.Plan.EffectiveStop };
                }

                return marked;
            })
            .ToArray();
        var marks = ledger.Equity;
        if (force || marks.Count == 0 || now - marks[^1].At >= TimeSpan.FromMinutes(1))
        {
            if (marks.Count > 0 && marks[^1].At == now)
            {
                marks = marks.Take(marks.Count - 1).ToArray();
            }

            marks = marks
                .Append(
                    new TradingEquityMark(
                        now,
                        TradingLedgerAccounting.Equity(ledger, books, now, rules.MaxDataAgeSeconds),
                        ledger.FundingThb
                    )
                )
                .ToArray();
        }

        return ledger with
        {
            Positions = positions,
            Equity = marks,
        };
    }

    internal static decimal? DailyResult(TradingLedger ledger, decimal? equity, DateTimeOffset now)
    {
        if (equity is null)
        {
            return null;
        }

        var today = now.ToOffset(TimeSpan.FromHours(7)).Date;
        var sells = ledger
            .Fills.Where(fill =>
                fill.Side == TradeSide.Sell && fill.OccurredAt.ToOffset(TimeSpan.FromHours(7)).Date == today
            )
            .ToArray();
        return sells.Any(fill => fill.RealizedNetThb is null)
            ? null
            : sells.Sum(fill => fill.RealizedNetThb ?? 0)
                + equity
                - ledger.CashThb
                - ledger.Positions.Sum(position => position.CostThb);
    }

    internal static decimal LossCap(TradingState state) =>
        state.Rules.Adaptive is { } policy
            ? (
                state.Mode == TradingMode.Paper
                    ? (state.Paper.Equity.Count > 0 ? state.Paper.Equity[^1].EquityThb : null) ?? state.Paper.FundingThb
                    : state.Autonomy?.Capital?.EquityThb ?? 0
            ) * policy.DailyLossFraction
            : state.Rules.DailyLossThb;

    internal static TradingState ApplyFunding(TradingState state, CapitalReadResult capital)
    {
        if (state.Mode != TradingMode.AutoLive)
        {
            return state;
        }

        var unseen = capital
            .Evidence.CashFlows.Where(flow => !state.ObservedFundingFlows.Contains(flow.Id, StringComparer.Ordinal))
            .ToArray();
        var adjustment = unseen
            .Where(flow => flow.Symbol == "THB")
            .Sum(flow => flow.Kind == CashFlowKind.Deposit ? flow.Quantity : -flow.Quantity);
        return state with
        {
            Live = state.Live with
            {
                CashThb = state.Live.CashThb + adjustment,
                FundingThb = state.Live.FundingThb + adjustment,
            },
            ObservedFundingFlows = state
                .ObservedFundingFlows.Concat(unseen.Select(flow => flow.Id))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        };
    }
}
