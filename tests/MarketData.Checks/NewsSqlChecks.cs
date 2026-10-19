using Microsoft.Data.SqlClient;
using Quantara.MarketData.Domain.News;
using Quantara.MarketData.Infrastructure.News;

namespace Quantara.MarketData.Checks;

public static class NewsSqlChecks
{
    public static async Task Run()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUANTARA_FEATURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("News SQL checks skipped: QUANTARA_FEATURE_SQL_CONNECTION is not set.");
            return;
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!builder.InitialCatalog.StartsWith("QuantaraFeatureChecks", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("News SQL checks require an isolated QuantaraFeatureChecks database.");
        }

        var store = new SqlNewsStore(connectionString, false);
        await store.Initialize(CancellationToken.None);
        var source = new NewsSource(
            "federal-reserve",
            "Federal Reserve",
            new Uri("https://www.federalreserve.gov/feeds/press_all.xml")
        );
        var eventId = $"sql-check-{Guid.NewGuid():N}";
        var firstSeen = DateTimeOffset.UtcNow.AddMinutes(-2);
        var first = CreateEvent(eventId, "headline one", firstSeen, firstSeen);
        await store.Save(
            source,
            [first],
            HealthyState(source, firstSeen),
            "etag-1",
            "modified-1",
            CancellationToken.None
        );
        await store.Save(
            source,
            [first],
            HealthyState(source, firstSeen.AddMinutes(1)),
            "etag-1",
            "modified-1",
            CancellationToken.None
        );
        var duplicate = await new SqlNewsStore(connectionString, false).Read(null, null, CancellationToken.None);
        Assert(
            duplicate.Events.Any(item => item.EventId == eventId && item.Revision == 1),
            "duplicate created a revision"
        );
        var second = first with { ContentHash = "changed", RevisionSeenAt = firstSeen.AddMinutes(2) };
        await store.Save(
            source,
            [second],
            HealthyState(source, firstSeen.AddMinutes(2)),
            "etag-2",
            "modified-2",
            CancellationToken.None
        );
        var cutoff = await new SqlNewsStore(connectionString, false).Read(
            null,
            firstSeen.AddMinutes(1),
            CancellationToken.None
        );
        Assert(cutoff.Events.Any(item => item.EventId == eventId && item.Revision == 1), "asOf missed revision one");
        var current = await new SqlNewsStore(connectionString, false).Read(null, null, CancellationToken.None);
        Assert(
            current.Events.Any(item =>
                item.EventId == eventId
                && item.Revision == 2
                && item.ContentHash == "changed"
                && item.FirstSeenAt == firstSeen
            ),
            "latest revision was not retained"
        );
        var restarted = new SqlNewsStore(connectionString, false);
        var validator = await restarted.ReadValidator(source.Id, CancellationToken.None);
        Assert(
            validator?.ETag == "etag-2" && validator.LastModified == "modified-2",
            "HTTP validators did not persist"
        );
        await restarted.Save(
            source,
            [],
            new NewsSourceState(
                source.Id,
                source.Label,
                source.Url.ToString(),
                "Unavailable",
                null,
                firstSeen.AddMinutes(10),
                "fixture failure"
            ),
            null,
            null,
            CancellationToken.None
        );
        validator = await new SqlNewsStore(connectionString, false).ReadValidator(source.Id, CancellationToken.None);
        Assert(
            validator?.ETag == "etag-2"
                && validator.LastModified == "modified-2"
                && validator.NextRetryAt == firstSeen.AddMinutes(10),
            "failure lost HTTP validators or retry"
        );
        var afterFailure = await restarted.Read(null, null, CancellationToken.None);
        var failedSource = afterFailure.SourceStates.Single(item => item.Id == source.Id);
        Assert(
            failedSource.Status == "Unavailable" && failedSource.LastSuccessAt == firstSeen.AddMinutes(2),
            "failure lost prior source success or hid outage"
        );
        Assert(!current.Pilot.Completed72Hours && current.Pilot.LastPollAt is not null, "pilot status was fabricated");
        await PollerLifecycle(connectionString, source);
    }

    private static async Task PollerLifecycle(string connection, NewsSource source)
    {
        var store = new SqlNewsStore(connection, false);
        await store.Save(
            source,
            [],
            HealthyState(source, DateTimeOffset.UtcNow),
            "etag-2",
            "modified-2",
            CancellationToken.None
        );
        var transport = new NewsPollerTransport();
        using var poller = new NewsPoller(store, transport, TimeProvider.System);
        await poller.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (
                (await store.ReadValidator("bls", CancellationToken.None))?.NextRetryAt is null
                && DateTimeOffset.UtcNow < deadline
            )
            {
                await Task.Delay(25);
            }

            var retry = await store.ReadValidator("bls", CancellationToken.None);
            Assert(retry?.NextRetryAt > DateTimeOffset.UtcNow.AddSeconds(100), "Retry-After was not persisted");
            Assert(
                transport.FedCalls == 1 && transport.BlsCalls == 1 && transport.Conditional,
                "conditional poll or source isolation failed"
            );
        }
        finally
        {
            await poller.StopAsync(CancellationToken.None);
        }

        var restarted = new NewsPollerTransport();
        using var resumed = new NewsPoller(new SqlNewsStore(connection, false), restarted, TimeProvider.System);
        await resumed.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (restarted.FedCalls == 0 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            var packet = await store.Read(null, null, CancellationToken.None);
            Assert(
                restarted.FedCalls == 1 && restarted.BlsCalls == 0 && restarted.Conditional,
                "restart ignored persisted retry or HTTP validators"
            );
            Assert(
                packet.SourceStates.Single(item => item.Id == "federal-reserve").Status == "NoNewEvents",
                "304 was not a successful unchanged source poll"
            );
            Assert(
                packet.SourceStates.Single(item => item.Id == "bls").Status == "RateLimited",
                "rate limit was reported as no news"
            );
        }
        finally
        {
            await resumed.StopAsync(CancellationToken.None);
        }
    }

    private static NewsEvent CreateEvent(
        string eventId,
        string title,
        DateTimeOffset firstSeen,
        DateTimeOffset revisionSeen
    ) =>
        new(
            eventId,
            1,
            "federal-reserve",
            title,
            "https://www.federalreserve.gov/a",
            firstSeen,
            firstSeen,
            revisionSeen,
            revisionSeen,
            revisionSeen.AddDays(1),
            Array.Empty<string>(),
            true,
            "macro",
            "FirstPartyClaim",
            title
        );

    private static NewsSourceState HealthyState(NewsSource source, DateTimeOffset success) =>
        new(source.Id, source.Label, source.Url.ToString(), "Healthy", success, null, null);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
