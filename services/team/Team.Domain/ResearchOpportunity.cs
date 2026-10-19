namespace Quantara.Team.Domain;

public sealed record ResearchOpportunity(
    string Symbol,
    string Reason,
    string ReconsiderWhen,
    DateTimeOffset ReviewAfter
);
