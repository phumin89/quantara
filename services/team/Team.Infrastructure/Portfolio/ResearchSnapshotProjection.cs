using System.Text.Json;
using static Quantara.Team.Infrastructure.PortfolioSnapshotJsonProjection;

namespace Quantara.Team.Infrastructure;

internal static class ResearchSnapshotProjection
{
    internal static object[] ReadCandidateObservations(JsonElement performance) =>
        performance.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array
            ? candidates
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Take(8)
                .Select(item => new
                {
                    symbol = ReadText(item, "symbol"),
                    at = ReadText(item, "at"),
                    reason = ReadText(item, "reason"),
                    strategyVersion = ReadText(item, "strategyVersion"),
                    score = ReadNumber(item, "score"),
                })
                .ToArray()
            : [];

    internal static object ReadReadiness(JsonElement value) =>
        value.TryGetProperty("readiness", out var source) && source.ValueKind == JsonValueKind.Object
            ? new
            {
                readyMarkets = ReadInteger(source, "readyMarkets"),
                unavailableMarkets = ReadInteger(source, "unavailableMarkets"),
                frames = ReadBoundedObjects(
                    source,
                    "frames",
                    6,
                    item => new
                    {
                        symbol = ReadText(item, "symbol"),
                        timeframeSeconds = ReadFrame(item, "timeframeSeconds"),
                        source = ReadText(item, "source"),
                        contiguousBars = ReadInteger(item, "contiguousBars"),
                        missingIntervals = ReadInteger(item, "missingIntervals"),
                        verifiedZeroVolumeBars = ReadInteger(item, "verifiedZeroVolumeBars"),
                        status = ReadText(item, "status"),
                        gapCause = ReadText(item, "gapCause"),
                        checkedAt = ReadText(item, "checkedAt"),
                        reason = ReadText(item, "reason"),
                    }
                ),
            }
            : new
            {
                readyMarkets = (int?)null,
                unavailableMarkets = (int?)null,
                frames = Array.Empty<object>(),
            };

    internal static object ReadFunnel(JsonElement value) =>
        value.TryGetProperty("funnel", out var source) && source.ValueKind == JsonValueKind.Object
            ? new
            {
                date = ReadText(source, "date"),
                coverage = ReadText(source, "coverage"),
                signals = ReadInteger(source, "signals"),
                submittedOrders = ReadInteger(source, "submittedOrders"),
                filledOrders = ReadInteger(source, "filledOrders"),
                closedTrades = ReadInteger(source, "closedTrades"),
                netThb = ReadBoundedDecimalString(source, "netThb"),
                blockedReasons = ReadBoundedObjects(
                    source,
                    "blockedReasons",
                    4,
                    item => new { reason = ReadText(item, "reason"), count = ReadInteger(item, "count") }
                ),
            }
            : new
            {
                date = (string?)null,
                coverage = (string?)null,
                signals = (int?)null,
                submittedOrders = (int?)null,
                filledOrders = (int?)null,
                closedTrades = (int?)null,
                netThb = (decimal?)null,
                blockedReasons = Array.Empty<object>(),
            };

    internal static object ReadEvaluation(JsonElement value) =>
        value.TryGetProperty("evaluation", out var source)
        && source.ValueKind == JsonValueKind.Object
        && ReadText(source, "protocol") is "ohlc-scenario-v1" or "ohlc-scenario-v2" or "ohlc-scenario-v3"
        && source.TryGetProperty("verifiedProfit", out var verified)
        && verified.ValueKind == JsonValueKind.False
            ? new
            {
                protocol = ReadText(source, "protocol"),
                verifiedProfit = false,
                observedAt = ReadText(source, "observedAt"),
                rulesHash = ReadText(source, "rulesHash"),
                warnings = ReadBoundedStrings(source, "warnings", 2, 1000),
                metrics = ReadValidationMetrics(source),
            }
            : new
            {
                protocol = (string?)null,
                verifiedProfit = false,
                observedAt = (string?)null,
                rulesHash = (string?)null,
                warnings = Array.Empty<string>(),
                metrics = Array.Empty<object>(),
            };

    private static int? ReadFrame(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.Number
        && item.TryGetInt32(out var number)
        && number is 300 or 900 or 3600
            ? number
            : null;

    private static int? ReadCount(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.Number
        && item.TryGetInt32(out var number)
        && number is >= 0 and <= 1_000_000
            ? number
            : null;

    private static string[] ReadBoundedStrings(JsonElement value, string property, int take, int max = 512) =>
        value.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array
            ? items
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .OfType<string>()
                .Where(item => item.Length <= max)
                .Take(take)
                .ToArray()
            : [];

    private static object[] ReadBoundedObjects(
        JsonElement value,
        string property,
        int take,
        Func<JsonElement, object> map
    ) =>
        value.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array
            ? items
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Take(take)
                .Select(map)
                .ToArray()
            : [];

    private static object[] ReadValidationMetrics(JsonElement value) =>
        value.TryGetProperty("metrics", out var items) && items.ValueKind == JsonValueKind.Array
            ? items
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && ReadText(item, "segment") == "validation")
                .Take(8)
                .Select(item => new
                {
                    symbol = ReadText(item, "symbol"),
                    timeframeSeconds = ReadFrame(item, "timeframeSeconds"),
                    strategy = ReadText(item, "strategy"),
                    samples = ReadCount(item, "samples"),
                    trades = ReadCount(item, "trades"),
                    unknownFills = ReadCount(item, "unknownFills"),
                    netThb = ReadNumber(item, "netThb"),
                    feesThb = ReadNumber(item, "feesThb"),
                    maxDrawdownThb = ReadNumber(item, "maxDrawdownThb"),
                    cashBenchmarkThb = ReadNumber(item, "cashBenchmarkThb"),
                    insufficientSamples = ReadBool(item, "insufficientSamples"),
                })
                .ToArray()
            : [];

    internal static Dictionary<string, object?>? ReadOpportunityReview(JsonElement performance)
    {
        if (
            !performance.TryGetProperty("opportunityReview", out var review)
            || review.ValueKind != JsonValueKind.Object
            || ReadText(review, "protocol") != "observed-book-v1"
            || !review.TryGetProperty("days", out var days)
            || days.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        return new()
        {
            ["protocol"] = "observed-book-v1",
            ["assumptions"] = ReadText(review, "assumptions"),
            ["waitingIncludesOpen"] = true,
            ["meanBasis"] = "closed-with-known-net",
            ["days"] = days.EnumerateArray()
                .Where(day => day.ValueKind == JsonValueKind.Object)
                .Take(7)
                .Select(day => new
                {
                    date = ReadText(day, "date"),
                    captured = ReadInteger(day, "captured"),
                    closed = ReadInteger(day, "closed"),
                    waiting = ReadInteger(day, "waiting"),
                    unknown = ReadInteger(day, "unknown"),
                    open = ReadInteger(day, "open"),
                    expired = ReadInteger(day, "expired"),
                    closedUnknown = ReadInteger(day, "closedUnknown"),
                    meanNetReturnFraction = day.TryGetProperty("meanNetReturnFraction", out var fraction)
                    && fraction.ValueKind == JsonValueKind.Number
                    && fraction.TryGetDecimal(out var value)
                    && value is >= -1 and <= 1000
                        ? value
                        : (decimal?)null,
                })
                .ToArray(),
        };
    }
}
