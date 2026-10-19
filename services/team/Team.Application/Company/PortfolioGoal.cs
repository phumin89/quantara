namespace Quantara.Team.Application;

public sealed record PortfolioGoal(
    string Id,
    decimal TargetThb,
    DateTimeOffset DeadlineExclusive,
    bool IncludesDeposits,
    string TimeZone
)
{
    public static PortfolioGoal Current { get; } =
        new(
            "portfolio-equity-2026-10",
            10000m,
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            true,
            "Asia/Bangkok"
        );
}
