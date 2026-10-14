namespace Quantara.Team.Domain;

public sealed record LearningSnapshot(
    DateTimeOffset ObservedAt,
    IReadOnlyList<LearningQuote> Markets,
    DateTimeOffset? EvaluationAt = null
);
