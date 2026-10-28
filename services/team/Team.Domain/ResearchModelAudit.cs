namespace Quantara.Team.Domain;

public sealed record ResearchModelAudit(
    string PolicyVersion,
    string RequestedModel,
    string Provider,
    string ReasoningEffort,
    string SelectionSource,
    string? ActualModel,
    string ActualModelSource,
    string? Fallback
);
