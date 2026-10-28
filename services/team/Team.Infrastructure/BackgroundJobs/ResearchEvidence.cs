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
                + AvailableBalanceText(balance)
            )
            .Order(StringComparer.Ordinal);
        return TeamResearchRouter.Digest(string.Join('|', rows));
    }

    private static string AvailableBalanceText(JsonElement balance)
    {
        if (!balance.TryGetProperty("available", out var available))
        {
            return "unknown";
        }

        return available.ValueKind == JsonValueKind.Number
            ? available.GetDecimal().ToString("G29", System.Globalization.CultureInfo.InvariantCulture)
            : available.GetRawText();
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

    public static IReadOnlyList<string> ScannedSymbols(JsonElement evidence)
    {
        if (
            !evidence.TryGetProperty("marketResearch", out var market)
            || !market.TryGetProperty("scan", out var scan)
            || !scan.TryGetProperty("symbols", out var symbols)
        )
        {
            return [];
        }

        return symbols
            .EnumerateArray()
            .Select(symbol => symbol.GetString())
            .OfType<string>()
            .Where(symbol => symbol.EndsWith("_THB", StringComparison.Ordinal))
            .Select(symbol => symbol[..^4])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static object Context(
        TeamRun run,
        JsonElement evidence,
        JsonElement news,
        bool includePositionSymbols = true
    )
    {
        var asOf = DateTimeOffset.UtcNow;
        // Allow the three-minute worker, final 45-second collection and role/audit persistence.
        var processingReviewAt = asOf.AddMinutes(6);
        var earliestReviewAt =
            processingReviewAt > run.CreatedAt.AddMinutes(15) ? processingReviewAt : run.CreatedAt.AddMinutes(15);
        return new
        {
            schemaVersion = "research-assessment-v1",
            runId = run.Id,
            accountKey = run.AccountKey,
            policyHash = PolicyHash(evidence),
            asOf,
            expiresAt = run.CreatedAt.AddHours(2),
            executionAuthority = false,
            evidenceIds = References(run, evidence, news),
            requiredPositionSymbols = includePositionSymbols ? HeldSymbols(evidence) : null,
            allowedOpportunitySymbols = includePositionSymbols ? ScannedSymbols(evidence) : null,
            entryThesis = "A frozen AI entry thesis is not recorded by this pipeline. Do not invent one or label all holdings legacy. Review supplied engine positionEvidence and actual entry/exit facts; missing original thesis alone does not prevent that scoped review.",
            earliestReviewAt,
        };
    }

    public static string? PolicyHash(JsonElement evidence) =>
        evidence.TryGetProperty("policyHash", out var hash) && hash.ValueKind == JsonValueKind.String
            ? hash.GetString()
            : null;
}
