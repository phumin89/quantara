namespace Quantara.Team.Application;

public sealed record ResearchBudgetDecision(bool Allowed, DateTimeOffset? NextAllowedAt, string Reason);
