namespace Quantara.Team.Application;

public sealed record PortfolioGoal(
    string Id,
    decimal TargetThb,
    DateTimeOffset DeadlineExclusive,
    bool IncludesDeposits,
    string TimeZone
);
