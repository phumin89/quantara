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
        var active = ActiveMarket(now, now.AddMinutes(-5));
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

        await NewsArrivesDuringRead();
        await MarketTimestampIsPreserved();
        await NewsExpiresDuringAccountRead();
        await FreshMarketSurvivesExpiredNews();
        await FreshNewsSurvivesExpiredMarket();
        await NoSignalSkipsAccountRead();
        await CallerCancelsDuringAccountRead();
    }

    private static async Task NewsArrivesDuringRead()
    {
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow();
        var packet = JsonSerializer.Serialize(new { events = new[] { Event("arriving", 1, now, now.AddSeconds(1)) } });
        var notice = await Reader(
                "{}",
                new AdvancingNewsReader(packet, () => time.Advance(TimeSpan.FromSeconds(2))),
                new FakeCurrentAccountReader(new string('a', 64)),
                time
            )
            .Read(CancellationToken.None);
        Check(
            notice is not null && notice.CheckedAt == now.AddSeconds(2),
            "news arriving during public reads is eligible"
        );
    }

    private static async Task MarketTimestampIsPreserved()
    {
        var time = new ManualTimeProvider();
        time.Advance(TimeSpan.FromMinutes(2));
        var now = time.GetUtcNow();
        var checkedAt = now.AddSeconds(-90);
        var market = ActiveMarket(checkedAt, now.AddMinutes(-7));
        var account = new string('a', 64);
        var notice = await Reader(
                market,
                new AdvancingNewsReader("{\"events\":[]}", () => time.Advance(TimeSpan.FromSeconds(1))),
                new AdvancingAccountReader(account, () => time.Advance(TimeSpan.FromSeconds(1))),
                time
            )
            .Read(CancellationToken.None);
        Check(
            notice is not null && notice.CheckedAt == checkedAt,
            "public and account reads do not refresh market time"
        );
        Check(
            notice?.Key == (await Reader(market, "{\"events\":[]}", account, time).Read(CancellationToken.None))?.Key,
            "fresh market identity is stable across collection delays"
        );
    }

    private static async Task NewsExpiresDuringAccountRead()
    {
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow();
        var seenAt = now.AddMinutes(-15).AddSeconds(1);
        var packet = JsonSerializer.Serialize(new { events = new[] { Event("expiring", 1, seenAt, seenAt) } });
        var account = new AdvancingAccountReader(new string('a', 64), () => time.Advance(TimeSpan.FromSeconds(2)));
        var notice = await Reader("{}", new FakeNewsReader(packet), account, time).Read(CancellationToken.None);
        Check(notice is null && account.Reads == 1, "news expiring during account reads does not wake research");
    }

    private static async Task FreshMarketSurvivesExpiredNews()
    {
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow();
        var market = ActiveMarket(now, now.AddMinutes(-5));
        var seenAt = now.AddMinutes(-15).AddSeconds(1);
        var packet = JsonSerializer.Serialize(new { events = new[] { Event("expiring", 2, now.AddDays(-1), seenAt) } });
        var account = new string('a', 64);
        var notice = await Reader(
                market,
                new FakeNewsReader(packet),
                new AdvancingAccountReader(account, () => time.Advance(TimeSpan.FromSeconds(2))),
                time
            )
            .Read(CancellationToken.None);
        var marketOnly = await Reader(market, "{\"events\":[]}", account, time).Read(CancellationToken.None);
        Check(
            notice is not null
                && marketOnly is not null
                && notice.Key == marketOnly.Key
                && notice.Description == marketOnly.Description,
            "fresh market survives an expired news revision without retaining its key"
        );
    }

    private static async Task FreshNewsSurvivesExpiredMarket()
    {
        var time = new ManualTimeProvider();
        time.Advance(TimeSpan.FromMinutes(2));
        var now = time.GetUtcNow();
        var market = ActiveMarket(now.AddSeconds(-119), now.AddMinutes(-7));
        var packet = JsonSerializer.Serialize(new { events = new[] { Event("fresh", 1, now, now) } });
        var account = new string('a', 64);
        var notice = await Reader(
                market,
                new FakeNewsReader(packet),
                new AdvancingAccountReader(account, () => time.Advance(TimeSpan.FromSeconds(2))),
                time
            )
            .Read(CancellationToken.None);
        var newsOnly = await Reader("{}", packet, account, time).Read(CancellationToken.None);
        Check(
            notice is not null
                && newsOnly is not null
                && notice.Key == newsOnly.Key
                && notice.Description == newsOnly.Description
                && notice.CheckedAt == now.AddSeconds(2),
            "fresh news survives an expired market without refreshing its timestamp or retaining its key"
        );
    }

    private static async Task NoSignalSkipsAccountRead()
    {
        var time = new ManualTimeProvider();
        var account = new AdvancingAccountReader(new string('a', 64), () => time.Advance(TimeSpan.FromSeconds(1)));
        var notice = await Reader("{}", new FakeNewsReader("{\"events\":[]}"), account, time)
            .Read(CancellationToken.None);
        Check(notice is null && account.Reads == 0, "no eligible public signal skips account reads");
    }

    private static async Task CallerCancelsDuringAccountRead()
    {
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow();
        var packet = JsonSerializer.Serialize(new { events = new[] { Event("fresh", 1, now, now) } });
        using var cancellation = new CancellationTokenSource();
        try
        {
            await Reader(
                    "{}",
                    new FakeNewsReader(packet),
                    new AdvancingAccountReader(new string('a', 64), () => cancellation.Cancel()),
                    time
                )
                .Read(cancellation.Token);
            throw new InvalidOperationException("caller cancellation during account reads was swallowed");
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

    private static string ActiveMarket(DateTimeOffset checkedAt, DateTimeOffset candleAt) =>
        JsonSerializer.Serialize(
            new
            {
                available = true,
                connected = true,
                checkedAt,
                signals = new[]
                {
                    new
                    {
                        id = "market-1",
                        symbol = "BTC_THB",
                        direction = "breakout",
                        candleAt = candleAt.ToUnixTimeSeconds(),
                        detectedAt = checkedAt,
                        price = 42m,
                    },
                },
            }
        );

    private static CompanyResearchSignalReader Reader(string market, string news, string account, TimeProvider time) =>
        Reader(market, new FakeNewsReader(news), new FakeCurrentAccountReader(account), time);

    private static CompanyResearchSignalReader Reader(
        string market,
        INewsEvidenceReader news,
        ICurrentAccountReader account,
        TimeProvider time
    ) =>
        new(
            new MarketSignalReader(
                new HttpClient(new NewsStubHandler(market)) { BaseAddress = new Uri("https://market-data.test") },
                time
            ),
            news,
            account,
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
