using System.Text.Json;
using static Quantara.Team.Infrastructure.PortfolioSnapshotJsonProjection;

namespace Quantara.Team.Infrastructure;

internal static class TradingSnapshotProjection
{
    internal static string? TradingMode(JsonElement? overview) =>
        overview is { } value
        && value.TryGetProperty("mode", out var mode)
        && mode.ValueKind == JsonValueKind.String
        && mode.GetString() is "stopped" or "paper" or "manualReview" or "autoLive"
            ? mode.GetString()
            : null;

    internal static Dictionary<string, object?> TradingPerformance(JsonElement? overview)
    {
        if (
            TradingMode(overview) is null
            || overview is not { } value
            || !value.TryGetProperty("autonomy", out var performance)
        )
        {
            return new() { ["available"] = false, ["reason"] = "unavailable" };
        }

        if (
            performance.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("heartbeatAt", out var heartbeat)
            || heartbeat.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(heartbeat.GetString(), out var heartbeatAt)
            || DateTimeOffset.UtcNow - heartbeatAt > TimeSpan.FromSeconds(120)
            || DateTimeOffset.UtcNow - heartbeatAt < TimeSpan.FromSeconds(-5)
        )
        {
            return new() { ["available"] = false, ["reason"] = "stale" };
        }

        var result = new Dictionary<string, object?>
        {
            ["available"] = true,
            ["mode"] = TradingMode(overview),
            ["heartbeatAt"] = heartbeatAt,
            ["fault"] = ReadText(value, "fault"),
            ["dailyProfitTargetThb"] = ReadBoundedDecimalString(performance, "dailyProfitTargetThb"),
            ["realizedNetTodayThb"] = ReadBoundedDecimalString(performance, "realizedNetTodayThb"),
            ["scanCoverage"] = new
            {
                totalMarkets = ReadInteger(performance, "totalMarkets"),
                eligibleMarkets = ReadInteger(performance, "eligibleMarkets"),
                checkedMarkets = ReadInteger(performance, "checkedMarkets"),
            },
            ["activity"] = ReadText(performance, "activity"),
            ["opportunityReview"] = ResearchSnapshotProjection.ReadOpportunityReview(performance),
            ["candidateReasons"] =
                performance.TryGetProperty("candidates", out var reasons) && reasons.ValueKind == JsonValueKind.Array
                    ? reasons
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.Object)
                        .Select(item => ReadText(item, "reason"))
                        .OfType<string>()
                        .Take(8)
                        .ToArray()
                    : Array.Empty<string>(),
            ["readiness"] = ResearchSnapshotProjection.ReadReadiness(performance),
            ["funnel"] = ResearchSnapshotProjection.ReadFunnel(performance),
            ["evaluation"] = ResearchSnapshotProjection.ReadEvaluation(performance),
            ["strategy"] = TradingStrategy(value),
            ["capital"] = ReadCapital(performance, value),
            ["positionEvidence"] = ReadPositionEvidence(value, heartbeatAt),
            ["candidateObservations"] = ResearchSnapshotProjection.ReadCandidateObservations(performance),
            ["pnlScope"] = new { basis = "selected-engine-ledger", includesExternalTrades = false },
        };
        return result;
    }

    internal static object? TradingStrategy(JsonElement? overview)
    {
        if (
            overview is not { } value
            || !value.TryGetProperty("rules", out var rules)
            || rules.ValueKind != JsonValueKind.Object
            || !rules.TryGetProperty("adaptive", out var adaptive)
            || adaptive.ValueKind != JsonValueKind.Object
        )
        {
            return null;
        }

        var version = ReadText(rules, "version");
        var profile =
            adaptive.TryGetProperty("risk", out var risk) && risk.ValueKind == JsonValueKind.Object
                ? ReadText(risk, "profile")
                : null;
        if (version is not ("adaptive-intraday-v1" or "adaptive-intraday-v2") || profile != "aggressive")
        {
            return null;
        }

        var limits = ReadRiskLimits(adaptive, risk);
        if (limits is null)
        {
            return null;
        }

        var v2 = version == "adaptive-intraday-v2";
        return new
        {
            version,
            executionOwner = "deterministic-engine",
            timeframesSeconds = v2 ? new[] { 300, 900, 3600 } : new[] { 300 },
            automaticExits = new
            {
                stop = true,
                target = true,
                trailing = true,
                time = true,
                orderExpiry = true,
            },
            sizing = new
            {
                profile = "aggressive",
                basis = "verified-account-capital",
                fixedOrderThb = (decimal?)null,
                maximumExposureFraction = limits.Value.MaximumExposure,
                maximumConcentrationFraction = limits.Value.MaximumConcentration,
                riskToStopFraction = limits.Value.RiskToStop,
                atrMultiplier = limits.Value.AtrMultiplier,
                estimatedFeeRate = limits.Value.EstimatedFeeRate,
                maximumSpreadFraction = limits.Value.MaximumSpread,
                maximumDataAgeSeconds = limits.Value.MaximumDataAge,
                maximumSignalAgeSeconds = limits.Value.MaximumSignalAge,
                dailyLossFraction = limits.Value.DailyLoss,
                includeFutureDeposits = limits.Value.IncludeFutureDeposits,
            },
        };
    }

    internal static string? TradingStrategyVersion(JsonElement overview) =>
        overview.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Object
            ? ReadText(rules, "version")
            : null;

    private static (
        decimal MaximumExposure,
        decimal MaximumConcentration,
        decimal RiskToStop,
        decimal AtrMultiplier,
        decimal EstimatedFeeRate,
        decimal MaximumSpread,
        int MaximumDataAge,
        int MaximumSignalAge,
        decimal DailyLoss,
        bool IncludeFutureDeposits
    )? ReadRiskLimits(JsonElement adaptive, JsonElement risk)
    {
        var maximumExposure = ReadFraction(risk, "maximumExposureFraction");
        var maximumConcentration = ReadFraction(risk, "maximumConcentrationFraction");
        var riskToStop = ReadFraction(risk, "riskToStopFraction");
        var atr = ReadNumber(risk, "atrMultiplier");
        var fee = ReadNumber(risk, "estimatedFeeRate");
        var spread = ReadNumber(risk, "maximumSpreadFraction");
        var dataAge = ReadPositiveInt(risk, "maximumDataAgeSeconds");
        var signalAge = ReadPositiveInt(risk, "maximumSignalAgeSeconds");
        var dailyLoss = ReadFraction(adaptive, "dailyLossFraction");
        var include = ReadBool(adaptive, "includeFutureDeposits");
        return
            maximumExposure is null or <= 0 or > 1
            || maximumConcentration is null or <= 0 or > 1
            || riskToStop is null or <= 0 or > 1
            || atr is null
            || atr is < 1m or > 10m
            || fee is null or < 0 or > .01m
            || spread is null or < 0 or > .1m
            || dataAge is null or < 10 or > 300
            || signalAge is null or < 60 or > 900
            || dailyLoss is null or <= 0 or > 1
            || include is not true
            ? null
            : (
                maximumExposure.Value,
                maximumConcentration.Value,
                riskToStop.Value,
                atr.Value,
                fee.Value,
                spread.Value,
                dataAge.Value,
                signalAge.Value,
                dailyLoss.Value,
                include.Value
            );
    }

    private static decimal? ReadFraction(JsonElement value, string property)
    {
        var number = ReadNumber(value, property);
        return number is >= 0 and <= 1 ? number : null;
    }

    private static int? ReadPositiveInt(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.Number
        && item.TryGetInt32(out var number)
        && number > 0
        && number <= 86400
            ? number
            : null;

    private static object ReadCapital(JsonElement performance, JsonElement overview)
    {
        var maximumAgeSeconds =
            overview.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Object
                ? ReadPositiveInt(rules, "maxDataAgeSeconds") ?? 120
                : 120;
        if (!performance.TryGetProperty("capital", out var capital) || capital.ValueKind != JsonValueKind.Object)
        {
            return new
            {
                observedAt = (string?)null,
                ageSeconds = (int?)null,
                maximumAgeSeconds,
                availableThb = (decimal?)null,
                equityThb = (decimal?)null,
                exposureThb = (decimal?)null,
                reservedThb = (decimal?)null,
                blocker = "unavailable",
            };
        }

        var observed = ReadText(capital, "observedAt");
        var valid =
            DateTimeOffset.TryParse(observed, out var at)
            && DateTimeOffset.UtcNow - at <= TimeSpan.FromSeconds(maximumAgeSeconds)
            && DateTimeOffset.UtcNow - at >= TimeSpan.FromSeconds(-5);
        return new
        {
            observedAt = DateTimeOffset.TryParse(observed, out _) ? observed : null,
            ageSeconds = DateTimeOffset.TryParse(observed, out var parsed)
                ? Math.Round((DateTimeOffset.UtcNow - parsed).TotalSeconds, 1)
                : (double?)null,
            maximumAgeSeconds,
            availableThb = valid ? ReadBoundedDecimalString(capital, "availableThb") : null,
            equityThb = valid ? ReadBoundedDecimalString(capital, "equityThb") : null,
            exposureThb = valid ? ReadBoundedDecimalString(capital, "exposureThb") : null,
            reservedThb = valid ? ReadBoundedDecimalString(capital, "reservedThb") : null,
            blocker = valid ? ReadText(capital, "blocker") : "staleOrInvalid",
        };
    }

    private static object ReadPositionEvidence(JsonElement overview, DateTimeOffset heartbeatAt)
    {
        var mode = TradingMode(overview);
        var bookName = mode == "paper" ? "paper" : "live";
        if (
            !overview.TryGetProperty(bookName, out var book)
            || book.ValueKind != JsonValueKind.Object
            || !book.TryGetProperty("positions", out var sourcePositions)
            || sourcePositions.ValueKind != JsonValueKind.Array
            || !book.TryGetProperty("orders", out var sourceOrders)
            || sourceOrders.ValueKind != JsonValueKind.Array
        )
        {
            return new
            {
                available = false,
                mode,
                executionAuthority = false,
            };
        }

        var positionItems = sourcePositions.EnumerateArray().ToArray();
        var orderItems = sourceOrders.EnumerateArray().ToArray();
        var positions = positionItems
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new
            {
                symbol = ReadText(item, "symbol"),
                quantity = ReadBoundedDecimalString(item, "quantity"),
                costThb = ReadBoundedDecimalString(item, "costThb"),
                openedAt = ReadText(item, "openedAt"),
                peakBid = ReadBoundedDecimalString(item, "peakBid"),
                exitPolicy = ReadText(item, "exitPolicy"),
                stopPrice = ReadBoundedDecimalString(item, "stopPrice"),
                targetPrice = ReadBoundedDecimalString(item, "targetPrice"),
                trailingDistance = ReadBoundedDecimalString(item, "trailingDistance"),
                trailingActive = ReadBool(item, "trailingActive"),
                timeExitAt = ReadText(item, "timeExitAt"),
            })
            .Where(item => item.symbol is not null && item.quantity is > 0)
            .Take(128)
            .ToArray();
        var orders = orderItems
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Where(item =>
                ReadText(item, "status")
                    is "pending"
                        or "submitting"
                        or "submitted"
                        or "partial"
                        or "partiallyFilled"
                        or "unknown"
            )
            .Take(128)
            .Select(item => new
            {
                symbol = ReadText(item, "symbol"),
                side = ReadText(item, "side"),
                remaining = ReadBoundedDecimalString(item, "remaining"),
                limitPrice = ReadBoundedDecimalString(item, "limitPrice"),
                createdAt = ReadText(item, "createdAt"),
                expiresAt = ReadText(item, "expiresAt"),
                status = ReadText(item, "status"),
            })
            .Take(128)
            .ToArray();
        return new
        {
            available = true,
            book = bookName,
            mode,
            heartbeatAt,
            executionAuthority = false,
            positions,
            positionOmittedCount = Math.Max(0, positionItems.Length - positions.Length),
            orders,
            orderOmittedCount = Math.Max(0, orderItems.Length - orders.Length),
        };
    }
}
