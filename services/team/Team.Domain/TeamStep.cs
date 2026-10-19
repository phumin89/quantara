namespace Quantara.Team.Domain;

public sealed record TeamStep(
    TeamRole Role,
    TeamRunStatus Status,
    string? Output,
    string? Error,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    string? InputDigest = null,
    ResearchUsage? Usage = null
);
