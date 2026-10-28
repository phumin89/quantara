using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public static class PolicyCandidateValidation
{
    public static ResearchReport Validate(
        ResearchReport report,
        TeamRole role,
        TeamRun run,
        string accountKey,
        string snapshot,
        DateTimeOffset now
    )
    {
        var candidate = report.PolicyCandidate;
        if (candidate is null)
        {
            return report;
        }

        using var document = JsonDocument.Parse(snapshot);
        var evidence = document.RootElement.TryGetProperty("evidence", out var nested) ? nested : document.RootElement;
        var hash = evidence.TryGetProperty("policyHash", out var hashElement) ? hashElement.GetString() : null;
        var ai =
            evidence.TryGetProperty("aiPolicy", out var aiElement)
            && aiElement.ValueKind == JsonValueKind.Object
            && aiElement.TryGetProperty("enabled", out var enabled)
            && enabled.ValueKind == JsonValueKind.True
            && aiElement.TryGetProperty("effectiveRulesHash", out var effective)
            && effective.GetString() == hash
            && evidence.TryGetProperty("tradingStrategy", out var strategy)
            && strategy.ValueKind == JsonValueKind.Object
            && strategy.TryGetProperty("version", out var version)
            && version.GetString() == "adaptive-intraday-v2";
        var assessment = report.Assessment;
        var parameters = candidate.Parameters;
        var valid =
            ai
            && role == TeamRole.Reviewer
            && assessment is not null
            && assessment.SchemaVersion == "research-assessment-v1"
            && assessment.AccountKey == accountKey
            && assessment.RunId == run.Id
            && assessment.PolicyHash == hash
            && candidate.RunId == run.Id
            && candidate.AccountKey == accountKey
            && candidate.BaselineRulesHash == hash
            && candidate.AsOf >= run.CreatedAt
            && candidate.AsOf <= now
            && candidate.ExpiresAt > now
            && candidate.ExpiresAt <= candidate.AsOf.AddMinutes(30)
            && candidate.EvidenceIds is { Count: >= 1 and <= 16 }
            && candidate.EvidenceIds.Distinct(StringComparer.Ordinal).Count() == candidate.EvidenceIds.Count
            && candidate.EvidenceIds.All(id => assessment.EvidenceIds.Contains(id, StringComparer.Ordinal))
            && parameters is not null
            && parameters.MaxPositions is >= 1 and <= 16
            && parameters.MaximumExposureFraction is > 0 and <= 1
            && parameters.MaximumConcentrationFraction is > 0 and <= 1
            && parameters.RiskToStopFraction is > 0 and <= 1
            && parameters.DailyLossFraction is > 0 and <= 1
            && parameters.MaxSpreadBps is >= 0 and <= 1000
            && parameters.SlippageBps is >= 0 and <= 1000
            && parameters.TakeProfitBps is >= 1 and <= 10000
            && parameters.StopLossBps is >= 1 and <= 10000
            && parameters.TrailingStopBps is >= 1 and <= 10000
            && parameters.VolumeRatio is >= 1 and <= 10
            && parameters.TimeExitMinutes is >= 1 and <= 1440
            && parameters.CooldownMinutes is >= 0 and <= 1440
            && parameters.OrderLifetimeMinutes is >= 1 and <= 1440
            && parameters.AtrMultiplier is >= 1 and <= 10
            && !string.IsNullOrWhiteSpace(candidate.Reason)
            && candidate.Reason.Length <= 800;
        return valid
            ? report
            : report with
            {
                PolicyCandidate = null,
                Output = report.Output + "\n\nPolicy candidate rejected by server validation.",
            };
    }
}
