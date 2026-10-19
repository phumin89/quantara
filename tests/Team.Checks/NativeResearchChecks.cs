using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class NativeResearchChecks
{
    private static readonly string[] symbols = ["ETH"];

    public static async Task Run()
    {
        using var handler = new NativeResearchHttpHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://market-data.invalid") };
        var reader = new MarketResearchReader(http);
        using var native = JsonDocument.Parse(
            await reader.ReadForMarkets(new string('a', 64), symbols, CancellationToken.None, true)
        );
        var evidence = native.RootElement.GetProperty("nativeExecution");
        var market = evidence.GetProperty("markets")[0];
        Check(
            evidence.GetProperty("available").GetBoolean()
                && !evidence.GetProperty("executionAuthority").GetBoolean()
                && market.GetProperty("timeframeSeconds").GetInt32() == 900
                && market.GetProperty("candles").GetArrayLength() == 51
                && market.GetProperty("candles")[0][0].GetInt64() % 900 == 0,
            "Native research must carry real completed 15m bars and no execution authority."
        );
        Check(
            native.RootElement.GetProperty("scan").GetProperty("symbols")[0].GetString() == "ETH_THB",
            "Native evidence must preserve the full-universe learning columns."
        );
        using var legacy = JsonDocument.Parse(
            await reader.ReadForMarkets(new string('a', 64), symbols, CancellationToken.None)
        );
        Check(
            handler.NativeCalls == 1 && !legacy.RootElement.TryGetProperty("nativeExecution", out _),
            "Manual/legacy research does not change its five-minute proposal contract."
        );
        using var badHandler = new NativeResearchHttpHandler { InvalidSource = true };
        using var badHttp = new HttpClient(badHandler) { BaseAddress = http.BaseAddress };
        using var invalid = JsonDocument.Parse(
            await new MarketResearchReader(badHttp).ReadForMarkets(
                new string('a', 64),
                symbols,
                CancellationToken.None,
                true
            )
        );
        Check(
            invalid.RootElement.GetProperty("available").GetBoolean()
                && !invalid.RootElement.GetProperty("nativeExecution").GetProperty("available").GetBoolean(),
            "Unverified native provenance blocks that evidence, not healthy base research."
        );
        Console.WriteLine(
            "Native read-only research: verified native candles, no financial authority, original scope and source failure isolation passed."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
