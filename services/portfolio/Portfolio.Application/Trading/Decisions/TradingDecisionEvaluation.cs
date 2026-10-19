using System.Globalization;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal static class TradingDecisionEvaluation
{
    internal static string[] RankMarkets(TradingState state, IReadOnlyList<TradingBook> books, DateTimeOffset now)
    {
        var ledger = state.Mode == TradingMode.AutoLive ? state.Live : state.Paper;
        return books
            .Select(book =>
            {
                var holding = ledger.Positions.SingleOrDefault(position =>
                    position.Symbol == book.Symbol && position.Quantity > 0
                );
                var decision = TradingDecisionEvaluation.Evaluate(state.Rules, book, holding, now);
                return (
                    book.Symbol,
                    Exit: decision.Action == TradingAction.Sell,
                    Score: AdaptiveTradingStrategy.OpportunityScore(state.Rules, book, decision)
                );
            })
            .OrderByDescending(item => item.Exit)
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .Select(item => item.Symbol)
            .ToArray();
    }

    internal static TradingExitPlan? StudySeed(
        TradingRuleSet rules,
        TradingBook book,
        TradingDecision decision,
        Guid intentId,
        DateTimeOffset now
    )
    {
        try
        {
            var trial = rules with { Version = "adaptive-intraday-v3", ExitPolicy = new() };
            return AdaptiveExitStrategy.Create(trial, book, decision, intentId, TradingRuleHash.Compute(trial), now);
        }
        catch (Exception error) when (error is ArgumentException or ArithmeticException)
        {
            // Research readiness never changes a validated v2 order decision.
            return null;
        }
    }

    internal static TradingDecision Evaluate(
        TradingRuleSet rules,
        TradingBook book,
        TradingHolding? holding,
        DateTimeOffset now
    ) =>
        rules.Adaptive is null
            ? TradingStrategy.Evaluate(rules, book, holding, now)
            : AdaptiveTradingStrategy.Evaluate(rules, book, holding, now);

    internal static string Key(TradingState state, string symbol, TradingDecision decision) =>
        state.Mode
        + ":"
        + state.RulesHash
        + ":"
        + symbol
        + ":"
        + (
            state.Rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3"
                ? decision.TimeframeSeconds.ToString(CultureInfo.InvariantCulture) + ":"
                : ""
        )
        + decision.Candle.ToString(CultureInfo.InvariantCulture)
        + ":"
        + decision.Action;
}
