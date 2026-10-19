namespace Quantara.Team.Domain;

public sealed record ResearchAssessment(
    string SchemaVersion,
    string AccountKey,
    Guid RunId,
    string? PolicyHash,
    DateTimeOffset AsOf,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<PositionAssessment> Positions,
    IReadOnlyList<ResearchOpportunity> Opportunities
);
