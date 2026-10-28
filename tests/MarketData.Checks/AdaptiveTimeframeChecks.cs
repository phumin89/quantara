using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class AdaptiveTimeframeChecks
{
    public static async Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        using var handler = new AdaptiveTimeframeHandler(now);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        var reader = new BitkubExecutionMarketReader(client, timeProvider: new FixedTimeProvider(now));

        handler.OmitLatestCompleted = true;
        using var incomplete = JsonDocument.Parse(
            await reader.Read(["BTC", "ETH", "XRP"], CancellationToken.None, true, true)
        );
        Check(
            incomplete.RootElement.GetProperty("markets").GetArrayLength() == 0,
            "Incomplete completed windows must remain unavailable instead of being cached as ready."
        );
        handler.OmitLatestCompleted = false;
        using var repaired = JsonDocument.Parse(
            await reader.Read(["BTC", "ETH", "XRP"], CancellationToken.None, true, true)
        );
        Check(
            repaired.RootElement.GetProperty("markets").GetArrayLength() == 3,
            "A repaired completed window in the same slot must become available."
        );
        Check(
            repaired.RootElement.GetProperty("markets")[0].GetProperty("timeframeSeconds").GetInt32() == 300
                && repaired.RootElement.GetProperty("markets")[1].GetProperty("timeframeSeconds").GetInt32() == 900
                && repaired.RootElement.GetProperty("markets")[2].GetProperty("timeframeSeconds").GetInt32() == 3600,
            "Repaired markets selected the wrong native timeframe."
        );
        var requestCountsAfterRepair = new Dictionary<string, int>
        {
            ["BTC:5"] = HistoryRequests(handler, "BTC", 5),
            ["ETH:15"] = HistoryRequests(handler, "ETH", 15),
            ["XRP:60"] = HistoryRequests(handler, "XRP", 60),
        };
        using var cachedRepair = JsonDocument.Parse(
            await reader.Read(["BTC", "ETH", "XRP"], CancellationToken.None, true, true)
        );
        Check(
            requestCountsAfterRepair.All(item =>
                HistoryRequests(
                    handler,
                    item.Key.Split(':')[0],
                    int.Parse(item.Key.Split(':')[1], CultureInfo.InvariantCulture)
                ) == item.Value
            ) && requestCountsAfterRepair.Values.All(count => count == 2),
            "A healthy completed window must be reused from cache."
        );

        using var document = JsonDocument.Parse(
            await reader.Read(["BTC", "ETH", "XRP"], CancellationToken.None, true, true)
        );
        var root = document.RootElement;
        var markets = root.GetProperty("markets");
        Check(markets.GetArrayLength() == 3, "Healthy adaptive markets must remain available concurrently.");
        Check(markets[0].GetProperty("timeframeSeconds").GetInt32() == 300, "BTC must select 300 seconds.");
        Check(markets[1].GetProperty("timeframeSeconds").GetInt32() == 900, "ETH must select 900 seconds.");
        Check(markets[2].GetProperty("timeframeSeconds").GetInt32() == 3600, "XRP must select 3600 seconds.");
        var evidence = root.GetProperty("evidence");
        Check(evidence.GetArrayLength() == 6, "Adaptive evidence must include each attempted timeframe.");
        Check(
            evidence.EnumerateArray().Count(item => item.GetProperty("status").GetString() == "ready") == 3,
            "Ready evidence count is wrong."
        );
        Check(
            evidence.EnumerateArray().Count(item => item.GetProperty("status").GetString() == "unavailable") == 3,
            "Unavailable evidence count is wrong."
        );
        foreach (var item in evidence.EnumerateArray())
        {
            Check(item.GetProperty("source").GetString() == "bitkub-tradingview", "Evidence source is wrong.");
            Check(item.GetProperty("checkedAt").GetDateTimeOffset() == now, "Evidence timestamp is wrong.");
            if (item.GetProperty("status").GetString() == "ready")
            {
                Check(item.GetProperty("gapCause").GetString() == "none", "Healthy evidence has a gap cause.");
                Check(item.GetProperty("completedBars").GetInt32() >= 51, "Completed bar count is too small.");
                Check(item.GetProperty("contiguousBars").GetInt32() >= 51, "Contiguous bar count is too small.");
                Check(item.GetProperty("missingIntervals").GetInt32() == 0, "Healthy evidence has missing intervals.");
                Check(
                    item.GetProperty("reason").ValueKind == JsonValueKind.Null,
                    "Healthy evidence has an error reason."
                );
            }
            else
            {
                Check(item.GetProperty("status").GetString() == "unavailable", "Unexpected evidence status.");
                Check(
                    item.GetProperty("gapCause").GetString() == "unknown",
                    "Unavailable evidence must have unknown gap cause."
                );
                Check(
                    item.GetProperty("reason").ValueKind == JsonValueKind.String,
                    "Unavailable evidence must explain its reason."
                );
            }
        }

        using var unavailableDocument = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(
                new HttpClient(new AdaptiveTimeframeHandler(now, unavailable: new HashSet<string> { "ETH" }))
                {
                    BaseAddress = new Uri("https://test.invalid"),
                },
                timeProvider: new FixedTimeProvider(now)
            ).Read(["BTC", "ETH"], CancellationToken.None, true, true)
        );
        Check(
            unavailableDocument.RootElement.GetProperty("markets").GetArrayLength() == 1,
            "Healthy market was suppressed by an unavailable peer."
        );
        Check(
            unavailableDocument.RootElement.GetProperty("errors").GetArrayLength() == 1,
            "Unavailable market error was not returned."
        );
        Check(
            unavailableDocument.RootElement.GetProperty("evidence").GetArrayLength() == 4,
            "Unavailable timeframe evidence was omitted."
        );

        var legacy = JsonDocument.Parse(await reader.Read(["BTC"], CancellationToken.None, true, false));
        Check(
            !legacy.RootElement.GetProperty("markets")[0].TryGetProperty("timeframeSeconds", out _),
            "Legacy output leaked timeframe metadata."
        );
        Check(!legacy.RootElement.TryGetProperty("evidence", out _), "Legacy output leaked adaptive evidence.");
        await CheckHistorical(reader, handler, now);
        Console.WriteLine("Adaptive timeframe checks passed.");
    }

    private static async Task CheckHistorical(
        BitkubExecutionMarketReader reader,
        AdaptiveTimeframeHandler handler,
        DateTimeOffset now
    )
    {
        var from = now.ToUnixTimeSeconds() / 900 * 900 - 900 * 10;
        using var result = JsonDocument.Parse(
            await reader.ReadHistorical("BTC", 900, from, from + 900 * 10, CancellationToken.None)
        );
        Check(
            result.RootElement.GetProperty("timeframeSeconds").GetInt32() == 900,
            "Historical resolution was not preserved."
        );
        Check(
            handler.Requests.Any(uri => uri.Query.Contains("resolution=15", StringComparison.Ordinal)),
            "Historical request used the wrong resolution."
        );
        foreach (var invalid in new[] { 0, 60, 301 })
        {
            try
            {
                await reader.ReadHistorical("BTC", invalid, from, from + 900, CancellationToken.None);
                throw new InvalidOperationException("Invalid timeframe accepted.");
            }
            catch (ArgumentException) { }
        }

        foreach (var timeframe in new[] { 300, 900, 3600 })
        {
            var shiftedHandler = new AdaptiveTimeframeHandler(now) { TimestampOffsetSeconds = 1 };
            using var shiftedClient = new HttpClient(shiftedHandler) { BaseAddress = new Uri("https://test.invalid") };
            var shiftedReader = new BitkubExecutionMarketReader(
                shiftedClient,
                timeProvider: new FixedTimeProvider(now)
            );
            var alignedFrom = now.ToUnixTimeSeconds() / timeframe * timeframe - timeframe * 10;
            await RejectsInvalidData(
                () =>
                    shiftedReader.ReadHistorical(
                        "BTC",
                        timeframe,
                        alignedFrom,
                        alignedFrom + timeframe * 10,
                        CancellationToken.None
                    ),
                $"Shifted {timeframe}-second candles were accepted."
            );
            var validHandler = new AdaptiveTimeframeHandler(now);
            using var validClient = new HttpClient(validHandler) { BaseAddress = new Uri("https://test.invalid") };
            var validReader = new BitkubExecutionMarketReader(validClient, timeProvider: new FixedTimeProvider(now));
            using var valid = JsonDocument.Parse(
                await validReader.ReadHistorical(
                    "BTC",
                    timeframe,
                    alignedFrom,
                    alignedFrom + timeframe * 10,
                    CancellationToken.None
                )
            );
            Check(
                valid.RootElement.GetProperty("error").ValueKind == JsonValueKind.Null,
                $"Aligned {timeframe}-second candles were rejected."
            );
        }

        try
        {
            await reader.ReadHistorical("BTC", 900, from + 1, from + 900, CancellationToken.None);
            throw new InvalidOperationException("Unaligned history accepted.");
        }
        catch (ArgumentException) { }

        try
        {
            await reader.ReadHistorical("BTC", 900, from, from + 32L * 86400, CancellationToken.None);
            throw new InvalidOperationException("Oversized history accepted.");
        }
        catch (ArgumentException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static int HistoryRequests(AdaptiveTimeframeHandler handler, string symbol, int minutes) =>
        handler.Requests.Count(uri =>
            uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal)
            && uri.Query.Contains($"symbol={symbol}_THB", StringComparison.Ordinal)
            && uri.Query.Contains($"resolution={minutes}", StringComparison.Ordinal)
        );

    private static async Task RejectsInvalidData(Func<Task<string>> operation, string message)
    {
        try
        {
            await operation();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
