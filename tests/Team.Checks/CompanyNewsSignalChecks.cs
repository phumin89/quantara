using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class CompanyNewsSignalChecks
{
    public static async Task Run()
    {
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow();
        var account = new string('a', 64);
        var inactive = JsonSerializer.Serialize(new { available = false, checkedAt = now });
        var active = JsonSerializer.Serialize(
            new
            {
                available = true,
                connected = true,
                checkedAt = now,
                signals = new[]
                {
                    new
                    {
                        id = "market-1",
                        symbol = "BTC_THB",
                        direction = "breakout",
                        candleAt = now.AddMinutes(-5).ToUnixTimeSeconds(),
                        detectedAt = now,
                        price = 42m,
                    },
                },
            }
        );
        var fresh = Event("n1", 1, now.AddMinutes(-1), now.AddMinutes(-1));
        var revised = Event("n2", 2, now.AddDays(-1), now.AddMinutes(-1));
        var old = Event("n3", 1, now.AddDays(-1), now.AddMinutes(-1));
        var future = Event("n4", 1, now.AddMinutes(1), now.AddMinutes(1));
        var packet = JsonSerializer.Serialize(new { events = new[] { fresh, revised } });
        var newsOnly = await Reader("{}", packet, account, time).Read(CancellationToken.None);
        Check(
            newsOnly is not null && newsOnly.Description.Contains("fresh news", StringComparison.Ordinal),
            "malformed market does not suppress fresh news"
        );
        var reversed = JsonSerializer.Serialize(new { events = new[] { revised, fresh } });
        Check(
            (await Reader(inactive, reversed, account, time).Read(CancellationToken.None))?.Key == newsOnly?.Key,
            "news ordering is stable"
        );
        Check(
            (await Reader(inactive, packet, new string('b', 64), time).Read(CancellationToken.None))?.Key
                != newsOnly?.Key,
            "news trigger is account isolated"
        );
        Check(
            await Reader(inactive, JsonSerializer.Serialize(new { events = new[] { old, future } }), account, time)
                .Read(CancellationToken.None)
                is null,
            "old catch-up and future events do not wake research"
        );
        Check(
            await Reader(active, "invalid-json", account, time).Read(CancellationToken.None) is not null,
            "news failure does not suppress market signal"
        );
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            await Reader(inactive, packet, account, time).Read(cancellation.Token);
            throw new InvalidOperationException("caller cancellation was swallowed");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private static object Event(string id, int revision, DateTimeOffset publishedAt, DateTimeOffset seenAt) =>
        new
        {
            eventId = id,
            revision,
            publishedAt,
            firstSeenAt = publishedAt,
            revisionSeenAt = seenAt,
        };

    private static CompanyResearchSignalReader Reader(string market, string news, string account, TimeProvider time) =>
        new(
            new MarketSignalReader(
                new HttpClient(new NewsStubHandler(market)) { BaseAddress = new Uri("https://market-data.test") },
                time
            ),
            new FakeNewsReader(news),
            new FakeCurrentAccountReader(account),
            time
        );

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
