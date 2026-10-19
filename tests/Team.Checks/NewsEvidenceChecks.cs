using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class NewsEvidenceChecks
{
    private static readonly string[] symbols = ["BTC"];

    public static async Task Run()
    {
        var observedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var valid = Packet(observedAt);
        Check(
            await new NewsEvidenceReader(Client(valid)).Read("BTC", CancellationToken.None) == valid,
            "full official news packet preserved"
        );
        var malformed = new[]
        {
            valid.Replace("news-v1", "news-v0", StringComparison.Ordinal),
            valid.Replace(
                "https://www.federalreserve.gov/feeds/press_all.xml",
                "http://evil.test/news",
                StringComparison.Ordinal
            ),
            valid.Replace(
                "https://www.federalreserve.gov/feeds/press_all.xml",
                "file:///secret",
                StringComparison.Ordinal
            ),
            valid.Replace(
                "https://www.federalreserve.gov/feeds/press_all.xml",
                "https://127.0.0.1/news",
                StringComparison.Ordinal
            ),
            valid.Replace(
                "https://www.federalreserve.gov/feeds/press_all.xml",
                "https://official@evil.com/news",
                StringComparison.Ordinal
            ),
            new string('x', 4001),
        };
        foreach (var body in malformed)
        {
            var unavailable = await new NewsEvidenceReader(Client(body)).Read(null, CancellationToken.None);
            Check(
                unavailable.Contains("invalid_packet", StringComparison.Ordinal)
                    || unavailable.Contains("packet_too_large", StringComparison.Ordinal),
                "malformed news is unavailable"
            );
        }
    }

    private static string Packet(DateTimeOffset observedAt) =>
        JsonSerializer.Serialize(
            new
            {
                schema = "news-v1",
                observedAt,
                executionAuthority = false,
                events = new[]
                {
                    new
                    {
                        eventId = "event-1",
                        revision = 1,
                        sourceId = "federal-reserve",
                        title = "Official release",
                        url = "https://www.federalreserve.gov/newsevents/pressreleases.htm",
                        publishedAt = observedAt.AddMinutes(-5),
                        firstSeenAt = observedAt.AddMinutes(-4),
                        revisionSeenAt = observedAt.AddMinutes(-3),
                        fetchedAt = observedAt.AddMinutes(-2),
                        expiresAt = observedAt.AddHours(2),
                        symbols,
                        marketWide = true,
                        category = "macro",
                        factStatus = "reported",
                        contentHash = "hash",
                    },
                },
                sourceStates = new[]
                {
                    new
                    {
                        id = "federal-reserve",
                        label = "Federal Reserve",
                        url = "https://www.federalreserve.gov/feeds/press_all.xml",
                        status = "Healthy",
                        lastSuccessAt = observedAt.AddMinutes(-2),
                        nextRetryAt = (DateTimeOffset?)null,
                        reason = (string?)null,
                    },
                },
                omittedEventCount = 0,
                pilot = new
                {
                    startedAt = observedAt.AddHours(-1),
                    lastPollAt = observedAt.AddMinutes(-2),
                    completed72Hours = false,
                    gapCount = 0,
                },
            }
        );

    private static HttpClient Client(string body) =>
        new(new NewsStubHandler(body)) { BaseAddress = new Uri("https://market-data.test") };

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
