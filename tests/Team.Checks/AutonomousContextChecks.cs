using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class AutonomousContextChecks
{
    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new string('a', 64);
        var portfolio = JsonSerializer.Serialize(
            new
            {
                positions = Array.Empty<object>(),
                summary = new
                {
                    totalAssets = 0,
                    availableThb = 5000,
                    holdingCount = 0,
                },
                updatedAt = now,
                priceUpdatedAt = now,
                historyUpdatedAt = now,
                historyCoverage = "Fixture",
                syncing = false,
                error = (string?)null,
            }
        );
        foreach (var stale in new[] { false, true })
        {
            using var handler = new SnapshotHttpHandler(
                portfolio,
                trading: JsonSerializer.Serialize(
                    new
                    {
                        accountKey = account,
                        mode = "autoLive",
                        heartbeatAt = stale ? now.AddMinutes(-5) : now,
                        ownerToken = "never-expose",
                        fault = (string?)null,
                        autonomy = new
                        {
                            dailyProfitTargetThb = "500",
                            realizedNetTodayThb = (string?)null,
                            totalMarkets = 100,
                            eligibleMarkets = 90,
                            checkedMarkets = 32,
                            activity = "Scanning markets",
                            candidates = Enumerable
                                .Range(0, 12)
                                .Select(_ => new { symbol = "ETH", reason = "No entry signal" }),
                        },
                    }
                )
            );
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://portfolio.invalid") };
            var result = await new PortfolioSnapshotReader(client).Read(CancellationToken.None);
            using var document = JsonDocument.Parse(result);
            var performance = document.RootElement.GetProperty("tradingPerformance");
            if (
                performance.GetProperty("available").GetBoolean() == stale
                || handler.TradingReads != 1
                || result.Contains("never-expose", StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "Research must project one fresh account-bound trading read and no authority fields."
                );
            }

            if (
                !stale
                && (
                    performance.GetProperty("mode").GetString() != "autoLive"
                    || performance.GetProperty("dailyProfitTargetThb").GetDecimal() != 500
                    || performance.GetProperty("realizedNetTodayThb").ValueKind != JsonValueKind.Null
                    || performance.GetProperty("candidateReasons").GetArrayLength() != 8
                )
            )
            {
                throw new InvalidOperationException(
                    "Actual adaptive context must preserve unknown profit and bound candidate details."
                );
            }
        }

        Console.WriteLine("Autonomous research context checks passed.");
    }
}
