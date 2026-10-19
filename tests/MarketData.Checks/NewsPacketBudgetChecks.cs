using System.Text.Json;
using Quantara.MarketData.Application.News;
using Quantara.MarketData.Domain.News;
using Quantara.MarketData.Infrastructure.News;

namespace Quantara.MarketData.Checks;

public static class NewsPacketBudgetChecks
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var events = Enumerable
            .Range(0, 9)
            .Select(index => new NewsEvent(
                $"event-{index}",
                1,
                "fed",
                new string('ก', 120),
                $"https://www.federalreserve.gov/{index}",
                now,
                now,
                now,
                now,
                now.AddDays(1),
                Array.Empty<string>(),
                true,
                "macro",
                "FirstPartyClaim",
                $"hash-{index}"
            ))
            .ToArray();
        var states = new[]
        {
            new NewsSourceState(
                "fed",
                "Federal Reserve",
                "https://www.federalreserve.gov/feed.xml",
                "Healthy",
                now,
                null,
                null
            ),
        };
        var packet = new NewsPacket(
            "news-v1",
            now,
            false,
            events,
            states,
            0,
            new NewsPilotStatus(now.AddHours(-72), now, false, 0)
        );
        var result = NewsPacketBudget.Fit(packet);
        var length = JsonSerializer.Serialize(result, NewsPacketBudgetChecks.jsonOptions).Length;
        Assert(result.Events.Count <= 5, "news budget exceeded event count");
        Assert(result.OmittedEventCount == events.Length - result.Events.Count, "news omissions were not disclosed");
        Assert(length <= 4000, "news packet exceeded serialized budget");
        Assert(
            result.SourceStates.Count == 1 && result.Pilot.LastPollAt == now,
            "source or pilot metadata was dropped"
        );
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
