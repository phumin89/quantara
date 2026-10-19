namespace Quantara.Portfolio.Domain.Trading;

public static class TradingTimeframes
{
    public static bool Supported(int seconds) => seconds is 300 or 900 or 3600;

    public static int For(TradingRuleSet rules, TradingBook book) =>
        rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3" ? book.TimeframeSeconds : 300;

    public static long LastCompletedOpen(DateTimeOffset now, int seconds)
    {
        if (!Supported(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        return now.ToUnixTimeSeconds() / seconds * seconds - seconds;
    }
}
