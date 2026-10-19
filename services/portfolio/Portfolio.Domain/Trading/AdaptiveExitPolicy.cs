namespace Quantara.Portfolio.Domain.Trading;

public sealed record AdaptiveExitPolicy
{
    public string Version { get; init; } = "adaptive-exit-v1";

    public int TrendReviewBars { get; init; } = 4;

    public int RangeReviewBars { get; init; } = 2;

    public int TrendMaximumMinutes { get; init; } = 480;

    public int RangeMaximumMinutes { get; init; } = 240;

    public int TrendMinimumMinutes { get; init; } = 120;

    public int RangeMinimumMinutes { get; init; } = 60;

    public int MaximumReviews { get; init; } = 24;

    public int NoProgressReviews { get; init; } = 3;

    public decimal TrendTrailingAtr { get; init; } = 2m;

    public decimal RangeTrailingAtr { get; init; } = 1.5m;

    public decimal ActivationAtr { get; init; } = 0.5m;

    public decimal MinimumProgressAtr { get; init; } = 0.25m;

    public void Validate()
    {
        if (
            this.Version != "adaptive-exit-v1"
            || this.TrendReviewBars is < 1 or > 24
            || this.RangeReviewBars is < 1 or > 24
            || this.TrendMaximumMinutes is < 1 or > 1440
            || this.RangeMaximumMinutes is < 1 or > 1440
            || this.TrendMinimumMinutes < 0
            || this.TrendMinimumMinutes >= this.TrendMaximumMinutes
            || this.RangeMinimumMinutes < 0
            || this.RangeMinimumMinutes >= this.RangeMaximumMinutes
            || this.MaximumReviews is < 1 or > 100
            || this.NoProgressReviews is < 1 or > 10
            || this.TrendTrailingAtr is <= 0m or > 10m
            || this.RangeTrailingAtr is <= 0m or > 10m
            || this.ActivationAtr is < 0m or > 10m
            || this.MinimumProgressAtr is < 0m or > 10m
        )
        {
            throw new ArgumentException("Invalid adaptive exit policy.");
        }
    }
}
