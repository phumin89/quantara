namespace Quantara.Team.Domain;

public sealed record ResearchTask(
    TeamRole Specialist,
    string InputDigest,
    string? PolicyHash,
    DateTimeOffset EvidenceAt,
    DateTimeOffset ExpiresAt,
    string? NewsDigest = null,
    string? StudyDigest = null,
    IReadOnlyList<string>? NewsRevisions = null
);
