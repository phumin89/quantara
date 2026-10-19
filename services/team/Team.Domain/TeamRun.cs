namespace Quantara.Team.Domain;

public sealed record TeamRun(
    Guid Id,
    string Objective,
    TeamRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TeamStep> Steps,
    string? Error,
    string? AccountKey = null,
    LearningSnapshot? Learning = null,
    LearningEvaluation? LearningEvaluation = null,
    LearningLesson? Lesson = null,
    LearningDecision? Decision = null,
    ResearchAssessment? Assessment = null
);
