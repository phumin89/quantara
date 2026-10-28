using System.Text.Json;
using System.Text.Json.Nodes;
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
        await CompletedPilotCases(observedAt);
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

    private static async Task CompletedPilotCases(DateTimeOffset observedAt)
    {
        var valid = CompletedPacket(observedAt);
        Check(
            await new NewsEvidenceReader(Client(valid)).Read(null, CancellationToken.None) == valid,
            "completed pilot accepts exact 72-hour window, fresh poll and required sources"
        );
        foreach (
            var accepted in new Action<JsonObject>[]
            {
                packet => Pilot(packet)["lastPollAt"] = observedAt.AddMinutes(-10).ToString("O"),
                packet => Pilot(packet)["lastPollAt"] = observedAt.AddSeconds(5).ToString("O"),
                packet => Pilot(packet)["gapCount"] = 3,
                packet => packet["sourceStates"] = Sources(observedAt.AddMinutes(-15), observedAt.AddMinutes(-15)),
                packet => packet["sourceStates"] = Sources(observedAt.AddSeconds(5), observedAt.AddSeconds(5)),
            }
        )
        {
            var acceptedBody = MutateCompleted(valid, accepted);
            Check(
                await new NewsEvidenceReader(Client(acceptedBody)).Read(null, CancellationToken.None) == acceptedBody,
                "valid completed pilot boundary was rejected"
            );
        }

        var rejected = new Action<JsonObject>[]
        {
            packet => Pilot(packet)["startedAt"] = observedAt.AddSeconds(-30).ToString("O"),
            packet => Pilot(packet)["startedAt"] = observedAt.AddHours(-71).ToString("O"),
            packet => Pilot(packet)["startedAt"] = observedAt.AddHours(-72).AddTicks(1).ToString("O"),
            packet => Pilot(packet)["startedAt"] = null,
            packet => Pilot(packet)["lastPollAt"] = null,
            packet => Pilot(packet)["lastPollAt"] = observedAt.AddMinutes(-11).ToString("O"),
            packet => Pilot(packet)["lastPollAt"] = observedAt.AddSeconds(6).ToString("O"),
            packet => Pilot(packet)["lastPollAt"] = observedAt.AddSeconds(5).AddTicks(1).ToString("O"),
            packet => Pilot(packet).Remove("lastPollAt"),
            packet => packet["sourceStates"] = Sources(null, observedAt.AddMinutes(-5)),
            packet => packet["sourceStates"] = Sources(observedAt.AddMinutes(-5), null),
            packet => packet["sourceStates"] = Sources(observedAt.AddMinutes(-16), observedAt.AddMinutes(-5)),
            packet => packet["sourceStates"] = Sources(observedAt.AddMinutes(-5), observedAt.AddSeconds(6)),
            packet => packet["sourceStates"] = Sources(observedAt.AddMinutes(-5), observedAt.AddSeconds(5).AddTicks(1)),
            packet =>
                packet["sourceStates"] = Sources(observedAt.AddMinutes(-5), observedAt.AddMinutes(-5), "Unavailable"),
            packet =>
                packet["sourceStates"] = Sources(observedAt.AddMinutes(-5), observedAt.AddMinutes(-5), duplicate: true),
            packet =>
                packet["sourceStates"] = Sources(
                    observedAt.AddMinutes(-5),
                    observedAt.AddMinutes(-5),
                    missingBls: true
                ),
        };
        foreach (var mutation in rejected)
        {
            var rejectedBody = MutateCompleted(valid, mutation);
            Check(rejectedBody != valid, "fixture mutation did not change packet");
            var unavailable = await new NewsEvidenceReader(Client(rejectedBody)).Read(null, CancellationToken.None);
            Check(
                unavailable.Contains("invalid_packet", StringComparison.Ordinal),
                "contradictory completed pilot metadata was accepted"
            );
        }

        var collecting = MutateCompleted(
            valid,
            packet =>
            {
                Pilot(packet)["completed72Hours"] = false;
                Pilot(packet)["startedAt"] = null;
                Pilot(packet)["lastPollAt"] = null;
                packet["sourceStates"] = Sources(null, null, "Unavailable");
            }
        );
        Check(
            await new NewsEvidenceReader(Client(collecting)).Read(null, CancellationToken.None) == collecting,
            "unfinished pilot retains nullable window and unavailable source semantics"
        );
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

    private static string CompletedPacket(DateTimeOffset observedAt)
    {
        var node =
            JsonNode.Parse(Packet(observedAt))?.AsObject()
            ?? throw new InvalidOperationException("Packet fixture missing.");
        var pilot = node["pilot"]?.AsObject() ?? throw new InvalidOperationException("Pilot fixture missing.");
        pilot["startedAt"] = observedAt.AddHours(-72).ToString("O");
        pilot["lastPollAt"] = observedAt.AddSeconds(-1).ToString("O");
        pilot["completed72Hours"] = true;
        var sources = node["sourceStates"]?.AsArray() ?? throw new InvalidOperationException("Source fixture missing.");
        sources.Add(
            new JsonObject
            {
                ["id"] = "bls",
                ["label"] = "BLS",
                ["url"] = "https://www.bls.gov/feed.xml",
                ["status"] = "NoNewEvents",
                ["lastSuccessAt"] = observedAt.AddMinutes(-5).ToString("O"),
                ["nextRetryAt"] = null,
                ["reason"] = null,
            }
        );
        return node.ToJsonString();
    }

    private static string MutateCompleted(string packet, Action<JsonObject> mutation)
    {
        var node = JsonNode.Parse(packet)?.AsObject() ?? throw new InvalidOperationException("Packet fixture missing.");
        mutation(node);
        return node.ToJsonString();
    }

    private static JsonObject Pilot(JsonObject packet) =>
        packet["pilot"]?.AsObject() ?? throw new InvalidOperationException("Pilot fixture missing.");

    private static JsonArray Sources(
        DateTimeOffset? federalSuccess,
        DateTimeOffset? blsSuccess,
        string status = "NoNewEvents",
        bool duplicate = false,
        bool missingBls = false
    )
    {
        var sources = new JsonArray
        {
            Source(
                "federal-reserve",
                "Federal Reserve",
                "https://www.federalreserve.gov/feeds/press_all.xml",
                status,
                federalSuccess
            ),
        };
        if (!missingBls)
        {
            sources.Add(Source("bls", "BLS", "https://www.bls.gov/feed.xml", status, blsSuccess));
        }

        if (duplicate)
        {
            sources.Add(
                Source(
                    "federal-reserve",
                    "Federal Reserve",
                    "https://www.federalreserve.gov/feeds/press_all.xml",
                    status,
                    federalSuccess
                )
            );
        }

        return sources;
    }

    private static JsonObject Source(string id, string label, string url, string status, DateTimeOffset? success) =>
        new()
        {
            ["id"] = id,
            ["label"] = label,
            ["url"] = url,
            ["status"] = status,
            ["lastSuccessAt"] = success?.ToString("O"),
            ["nextRetryAt"] = null,
            ["reason"] = null,
        };

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
