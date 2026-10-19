using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed record ResearchBudgetDecision(
    bool Allowed,
    DateTimeOffset? NextAllowedAt,
    string Reason,
    TeamRole? NextRole = null
);
