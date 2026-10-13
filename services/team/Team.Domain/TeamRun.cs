namespace Quantara.Team.Domain;

public sealed record TeamRun(
    Guid Id,
    string Objective,
    TeamRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TeamStep> Steps,
    string? Error,
    string? AccountKey = null
);
