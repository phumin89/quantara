namespace Quantara.Team.Domain;

public sealed record TeamStep(TeamRole Role, TeamRunStatus Status, string? Output, string? Error);
