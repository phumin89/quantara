namespace Quantara.Portfolio.Domain;

public static class PortfolioObjective
{
    public const string Id = "portfolio-equity-2026-10";

    public const decimal TargetThb = 10000m;

    public const int MaximumAgeSeconds = 90;

    public static DateTimeOffset StartsAt { get; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(7));

    public static DateTimeOffset DeadlineExclusive { get; } = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(7));

    public static DateTimeOffset Cutoff(DateTimeOffset now) =>
        now < PortfolioObjective.DeadlineExclusive ? now : PortfolioObjective.DeadlineExclusive.AddTicks(-1);
}
