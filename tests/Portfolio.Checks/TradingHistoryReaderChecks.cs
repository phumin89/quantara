using System.Text.Json;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingHistoryReaderChecks
{
    public static async Task Run()
    {
        var from = DateTimeOffset.UnixEpoch.AddDays(1);
        var until = from.AddDays(7);
        using var handler = new TradingHistoryHttpHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid/") };
        var reader = new MarketDataTradingHistoryReader(client);
        var bars = await reader.Read("BTC", 300, from, until, CancellationToken.None);
        Check(handler.Requests.Count == 3, "Seven-day history must use bounded 999-candle chunks.");
        Check(
            handler.Requests.All(uri => uri.Query.Contains("timeframeSeconds=300", StringComparison.Ordinal)),
            "Native timeframe was not preserved."
        );
        Check(
            handler.Requests.All(uri =>
                long.Parse(
                    Uri.UnescapeDataString(uri.Query.Split("to=")[1]),
                    System.Globalization.CultureInfo.InvariantCulture
                ) % 300
                == 0
            ),
            "Chunk end was not aligned."
        );
        Check(bars.Count == 2016, "Chunk candles were not combined.");

        using var emptyHandler = new TradingHistoryHttpHandler { Empty = true };
        using var emptyClient = new HttpClient(emptyHandler) { BaseAddress = client.BaseAddress };
        Check(
            (
                await new MarketDataTradingHistoryReader(emptyClient).Read(
                    "BTC",
                    300,
                    from,
                    from.AddHours(1),
                    CancellationToken.None
                )
            ).Count == 0,
            "Valid empty window was rejected."
        );

        foreach (var mode in new[] { "Source", "Symbol", "Timeframe", "Duplicate", "OutOfRange", "Malformed", "Error" })
        {
            using var badHandler = new TradingHistoryHttpHandler { FailureMode = mode };
            using var badClient = new HttpClient(badHandler) { BaseAddress = client.BaseAddress };
            try
            {
                await new MarketDataTradingHistoryReader(badClient).Read(
                    "BTC",
                    300,
                    from,
                    from.AddHours(1),
                    CancellationToken.None
                );
                throw new InvalidOperationException($"Malformed history {mode} was accepted.");
            }
            catch (InvalidDataException) { }
        }

        Console.WriteLine("Trading history reader checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
