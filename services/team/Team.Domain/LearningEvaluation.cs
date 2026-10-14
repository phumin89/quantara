namespace Quantara.Team.Domain;

public sealed record LearningEvaluation(
    DateTimeOffset TargetAt,
    DateTimeOffset ObservedAt,
    Guid SourceRunId,
    IReadOnlyList<LearningQuote> Markets
);
