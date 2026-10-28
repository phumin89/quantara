using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class CandleParserChecks
{
    public static void Run()
    {
        foreach (var interval in new[] { 300, 900, 3600 })
        {
            using var aligned = History(interval, 0);
            var candles = CandleParser.Parse(aligned.RootElement, 0, interval * 4, interval);
            if (candles.Count != 2 || candles[1].Timestamp != interval * 2)
            {
                throw new InvalidOperationException("Aligned native intraday candles must retain their timestamps.");
            }

            using var shifted = History(interval, 1);
            try
            {
                CandleParser.Parse(shifted.RootElement, 0, interval * 4, interval);
                throw new InvalidOperationException($"Shifted {interval}-second source candles were accepted.");
            }
            catch (InvalidDataException) { }
        }

        // Daily sessions retain their provider-defined boundary; only native intraday grids are validated here.
        using var daily = History(86400, 61200);
        var dailyCandles = CandleParser.Parse(daily.RootElement, 0, 86400 * 4);
        if (dailyCandles.Count != 2 || dailyCandles[0].Timestamp != 86400 + 61200)
        {
            throw new InvalidOperationException("Daily source timestamps must not be realigned to an intraday grid.");
        }

        Console.WriteLine("Candle parser boundary checks passed.");
    }

    private static JsonDocument History(int interval, int offset) =>
        JsonDocument.Parse(
            FormattableString.Invariant(
                $$"""
                {"s":"ok","t":[{{interval + offset}},{{interval * 2
                    + offset}}],"o":[10,10],"h":[12,12],"l":[9,9],"c":[11,11],"v":[1,1]}
                """
            )
        );
}
