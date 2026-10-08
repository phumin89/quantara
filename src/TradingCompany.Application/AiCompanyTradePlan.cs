using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record AiCompanyTradePlan(
    decimal EntryPrice,
    decimal Quantity,
    decimal TakeProfitPrice,
    decimal StopLossPrice,
    DateTimeOffset ExpiresAt);

public sealed record AiCompanyPriceLevels(
    decimal Low20,
    decimal High20,
    decimal AverageTrueRange14)
{
    public static AiCompanyPriceLevels From(IReadOnlyList<HistoricalBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count < 20) throw new ArgumentException("At least 20 bars are required.", nameof(bars));
        var ordered = bars.OrderBy(bar => bar.Date).ToArray();
        var recent = ordered[^20..];
        var atrBars = ordered[^14..];
        var atr = atrBars.Select((bar, index) =>
        {
            var previousClose = index == 0 ? ordered[^15].Close : atrBars[index - 1].Close;
            return Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
        }).Average();
        return new(recent.Min(bar => bar.Low), recent.Max(bar => bar.High), atr);
    }
}
