using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public static class ResearchAssessmentValidator
{
    public static void Validate(ResearchAssessment assessment, TeamRun current, string accountKey, string context)
    {
        using var document = JsonDocument.Parse(context);
        var root = document.RootElement;
        var evidence = root.GetProperty("evidence");
        var expected = root.GetProperty("assessmentContext");
        var now = DateTimeOffset.UtcNow;
        if (
            assessment.SchemaVersion != "research-assessment-v1"
            || assessment.AccountKey != accountKey
            || assessment.RunId != current.Id
            || assessment.PolicyHash != ResearchEvidence.PolicyHash(evidence)
            || assessment.AsOf != expected.GetProperty("asOf").GetDateTimeOffset()
        )
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.AssessmentLineage);
        }

        if (
            assessment.AsOf < current.CreatedAt.AddMinutes(-10)
            || assessment.AsOf > now.AddSeconds(5)
            || assessment.ExpiresAt <= now
            || assessment.ExpiresAt > current.CreatedAt.AddHours(2)
            || assessment.ExpiresAt > expected.GetProperty("expiresAt").GetDateTimeOffset()
            || assessment.ExpiresAt <= assessment.AsOf
        )
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.AssessmentLifetime);
        }

        if (
            assessment.EvidenceIds.Count is < 1 or > 16
            || assessment.Positions.Count > 64
            || assessment.Opportunities.Count > 16
        )
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.AssessmentBounds);
        }

        var allowed = ResearchEvidence
            .References(current, evidence, root.GetProperty("news"))
            .ToHashSet(StringComparer.Ordinal);
        if (
            assessment.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != assessment.EvidenceIds.Count
            || assessment.EvidenceIds.Any(reference => !allowed.Contains(reference))
        )
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.EvidenceReferences);
        }

        var held = ResearchEvidence.HeldSymbols(evidence).ToHashSet(StringComparer.Ordinal);

        var scanned = ResearchEvidence.ScannedSymbols(evidence).ToHashSet(StringComparer.Ordinal);

        if (
            !held.SetEquals(assessment.Positions.Select(position => position.Symbol))
            || assessment.Positions.Select(position => position.Symbol).Distinct(StringComparer.Ordinal).Count()
                != assessment.Positions.Count
        )
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.PositionCoverage);
        }

        if (
            assessment.Positions.Any(position =>
                position.Action is not ("Keep" or "Reduce" or "Exit" or "Review")
                || !Text(position.Thesis, 800)
                || !Text(position.Reason, 800)
                || position.ReviewAfter <= now
                || position.ReviewAfter >= position.ExpiresAt
                || position.ExpiresAt > assessment.ExpiresAt
                || position.MissingFields.Count > 12
                || position.MissingFields.Any(field => !Text(field, 80))
            )
        )
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.PositionContent);
        }

        if (
            assessment.Opportunities.Select(item => item.Symbol).Distinct(StringComparer.Ordinal).Count()
            != assessment.Opportunities.Count
        )
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.OpportunityDuplicate);
        }

        if (assessment.Opportunities.Any(item => !scanned.Contains(item.Symbol)))
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.OpportunitySymbol);
        }

        if (assessment.Opportunities.Any(item => !Text(item.Reason, 800) || !Text(item.ReconsiderWhen, 800)))
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.OpportunityText);
        }

        if (assessment.Opportunities.Any(item => item.ReviewAfter <= now || item.ReviewAfter >= assessment.ExpiresAt))
        {
            throw new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.OpportunityReviewTime);
        }
    }

    private static bool Text(string text, int maximum) => !string.IsNullOrWhiteSpace(text) && text.Length <= maximum;
}
