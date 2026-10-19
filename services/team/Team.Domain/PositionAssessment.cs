namespace Quantara.Team.Domain;

public sealed record PositionAssessment(
    string Symbol,
    string Action,
    string Thesis,
    string Reason,
    DateTimeOffset ReviewAfter,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> MissingFields
);
