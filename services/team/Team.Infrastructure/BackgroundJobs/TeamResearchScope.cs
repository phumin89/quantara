using System.Text.Json.Nodes;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public static class TeamResearchScope
{
    public static string Project(TeamRole role, string evidence)
    {
        if (role == TeamRole.Reviewer)
        {
            return evidence;
        }

        var root =
            JsonNode.Parse(evidence)?.AsObject() ?? throw new InvalidDataException("Research evidence is missing.");
        root.Remove("history");
        root.Remove("tradeHistory");
        root.Remove("historyCoverage");
        if (role == TeamRole.News)
        {
            root["heldSymbols"] = new JsonArray(
                ResearchEvidence
                    .HeldSymbols(System.Text.Json.JsonSerializer.SerializeToElement(root))
                    .Select(symbol => (JsonNode?)JsonValue.Create(symbol))
                    .ToArray()
            );
            root.Remove("walletEvidence");
            root.Remove("positions");
            root.Remove("summary");
            root.Remove("marketResearch");
            root.Remove("tradingPerformance");
        }
        else if (role == TeamRole.Quant)
        {
            root.Remove("marketResearch");
        }
        else if (root["marketResearch"] is JsonObject market)
        {
            if (role != TeamRole.Swing)
            {
                market.Remove("historicalCandles");
            }

            if (role == TeamRole.Swing)
            {
                market.Remove("intraday");
            }
        }

        return root.ToJsonString();
    }
}
