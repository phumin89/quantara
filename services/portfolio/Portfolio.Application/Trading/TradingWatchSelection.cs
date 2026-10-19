using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingWatchSelection
{
    public static IReadOnlyList<TradingScanObservation> Candidates(TradingState state, DateTimeOffset now)
    {
        if (state.Rules.Adaptive is not { } adaptive || state.Autonomy is not { } autonomy)
        {
            return [];
        }

        var excluded = adaptive.ExcludedSymbols.ToHashSet(StringComparer.Ordinal);
        return autonomy
            .Markets.Where(item =>
                !excluded.Contains(item.Symbol)
                && item.At <= now
                && now - item.At <= TimeSpan.FromSeconds(adaptive.Risk.MaximumSignalAgeSeconds)
            )
            .GroupBy(item => item.Symbol, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.At).First())
            .OrderByDescending(item => item.Score ?? decimal.MinValue)
            .ThenByDescending(item => item.At)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .Take(16)
            .ToArray();
    }

    public static IReadOnlyList<string> Required(TradingState state, DateTimeOffset now)
    {
        var owned = state
            .Live.Positions.Concat(state.Paper.Positions)
            .Where(position => position.Quantity > 0)
            .Select(position => position.Symbol)
            .Concat(
                state
                    .Live.Orders.Concat(state.Paper.Orders)
                    .Where(TradingLedgerAccounting.Active)
                    .Select(order => order.Symbol)
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (state.Rules.Adaptive is null)
        {
            return owned
                .Concat(state.Rules.Symbols)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        var research = state
            .ExitStudies.Where(sample => sample.Status is "active" or "triggered")
            .Select(sample => sample.Symbol)
            .Except(owned, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Min(16, Math.Max(0, 64 - owned.Length)))
            .ToArray();
        var capacity = Math.Max(0, 64 - owned.Length - research.Length);
        return owned
            .Concat(research)
            .Concat(
                Candidates(state, now)
                    .Where(item => item.Score > 0)
                    .Take(Math.Min(8, capacity))
                    .Select(item => item.Symbol)
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
