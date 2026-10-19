using System.Text.Json;
using Quantara.MarketData.Application.News;

namespace Quantara.MarketData.Infrastructure.News;

public static class NewsPacketBudget
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public static NewsPacket Fit(NewsPacket packet)
    {
        var selected = packet with
        {
            Events = packet.Events.Take(5).ToArray(),
            OmittedEventCount = packet.OmittedEventCount + Math.Max(0, packet.Events.Count - 5),
        };
        while (
            JsonSerializer.Serialize(selected, NewsPacketBudget.jsonOptions).Length > 4000 && selected.Events.Count > 0
        )
        {
            selected = selected with
            {
                Events = selected.Events.Take(selected.Events.Count - 1).ToArray(),
                OmittedEventCount = selected.OmittedEventCount + 1,
            };
        }

        if (JsonSerializer.Serialize(selected, NewsPacketBudget.jsonOptions).Length > 4000)
        {
            throw new InvalidDataException("News source metadata exceeded the context budget.");
        }

        return selected;
    }
}
