namespace Quantara.Team.Domain;

public sealed record CompanySchedule(
    bool Enabled,
    DateTimeOffset NextRunAt,
    Guid? LastRunId,
    string? LastSignalKey = null,
    DateTimeOffset? LastAnalysisAt = null,
    string? DeferredReason = null,
    TeamRole? NextRole = null
);
