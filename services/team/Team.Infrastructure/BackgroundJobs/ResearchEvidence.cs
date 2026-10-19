using System.Text.Json;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public static class ResearchEvidence
{
    public static string? Mode(JsonElement evidence) =>
        evidence.TryGetProperty("tradingMode", out var mode) && mode.ValueKind == JsonValueKind.String
            ? mode.GetString()
            : null;

    public static string HoldingsDigest(JsonElement evidence)
    {
        var balances = evidence.TryGetProperty("walletEvidence", out var wallet)
            ? wallet.GetProperty("balances")
            : evidence.GetProperty("positions");
        var rows = balances
            .EnumerateArray()
            .Select(balance =>
                balance.GetProperty("symbol").GetString()
                + ":"
                + balance
                    .GetProperty("quantity")
                    .GetDecimal()
                    .ToString("G29", System.Globalization.CultureInfo.InvariantCulture)
                + ":"
                + (balance.TryGetProperty("available", out var available) ? available.GetRawText() : "unknown")
            )
            .Order(StringComparer.Ordinal);
        return TeamResearchRouter.Digest(string.Join('|', rows));
    }

    public static IReadOnlyList<string> HeldSymbols(JsonElement evidence)
    {
        var balances = evidence.TryGetProperty("walletEvidence", out var wallet)
            ? wallet.GetProperty("balances")
            : evidence.GetProperty("positions");
        return balances
            .EnumerateArray()
            .Where(balance => balance.GetProperty("quantity").GetDecimal() > 0)
            .Select(balance =>
                balance.GetProperty("symbol").GetString() ?? throw new InvalidDataException("Held symbol is missing.")
            )
            .Where(symbol => symbol != "THB")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> References(TeamRun run, JsonElement evidence, JsonElement news)
    {
        var references = new List<string> { "portfolio:" + run.Id.ToString("D") };
        if (
            evidence.TryGetProperty("marketResearch", out var market)
            && market.TryGetProperty("available", out var available)
            && available.ValueKind == JsonValueKind.True
        )
        {
            references.Add("market:" + run.Id.ToString("D"));
        }

        if (news.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in events.EnumerateArray())
            {
                references.Add(
                    "news:"
                        + item.GetProperty("eventId").GetString()
                        + "@"
                        + item.GetProperty("revision")
                            .GetInt32()
                            .ToString(System.Globalization.CultureInfo.InvariantCulture)
                );
            }
        }

        return references;
    }

    public static object Context(TeamRun run, JsonElement evidence, JsonElement news) =>
        new
        {
            schemaVersion = "research-assessment-v1",
            runId = run.Id,
            accountKey = run.AccountKey,
            policyHash = PolicyHash(evidence),
            asOf = DateTimeOffset.UtcNow,
            expiresAt = run.CreatedAt.AddHours(2),
            executionAuthority = false,
            evidenceIds = References(run, evidence, news),
            entryThesis = "A frozen AI entry thesis is not recorded by this pipeline. Do not invent one or label all holdings legacy. Review supplied engine positionEvidence and actual entry/exit facts; missing original thesis alone does not prevent that scoped review.",
            earliestReviewAt = DateTimeOffset.UtcNow.AddMinutes(1) > run.CreatedAt.AddMinutes(15)
                ? DateTimeOffset.UtcNow.AddMinutes(1)
                : run.CreatedAt.AddMinutes(15),
        };

    public static string? PolicyHash(JsonElement evidence) =>
        evidence.TryGetProperty("policyHash", out var hash) && hash.ValueKind == JsonValueKind.String
            ? hash.GetString()
            : null;
}
