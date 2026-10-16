using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingOpportunityReview
{
    public static IReadOnlyList<TradingShadowSummary> Days(TradingState state, DateTimeOffset now) =>
        state
            .Shadows.Where(sample => sample.CreatedAt >= now.AddDays(-7))
            .GroupBy(sample =>
                sample
                    .CreatedAt.ToOffset(TimeSpan.FromHours(7))
                    .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            )
            .OrderByDescending(group => group.Key)
            .Take(7)
            .Select(group =>
            {
                var closed = group
                    .Where(sample => sample.State == TradingShadowState.Closed && sample.NetThb is not null)
                    .ToArray();
                return new TradingShadowSummary(
                    group.Key,
                    group.Count(),
                    closed.Length,
                    group.Count(sample => sample.State is TradingShadowState.Waiting or TradingShadowState.Open),
                    group.Count(sample => sample.State == TradingShadowState.Unknown),
                    closed.Length == 0 ? null : closed.Average(sample => sample.NetThb / sample.EntryCostThb)
                );
            })
            .ToArray();
}
