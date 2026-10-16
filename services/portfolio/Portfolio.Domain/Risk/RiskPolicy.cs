namespace Quantara.Portfolio.Domain.Risk;

public sealed record RiskPolicy
{
    public RiskProfile Profile { get; init; } = RiskProfile.Disabled;

    public decimal MaximumExposureFraction { get; init; } = 1m;

    public decimal MaximumConcentrationFraction { get; init; } = 1m;

    public decimal RiskToStopFraction { get; init; } = 0.05m;

    public decimal AtrMultiplier { get; init; } = 1m;

    public decimal MaximumSpreadFraction { get; init; } = 0.005m;

    public decimal EstimatedFeeRate { get; init; } = 0.0025m;

    public int MaximumDataAgeSeconds { get; init; } = 90;

    public int MaximumSignalAgeSeconds { get; init; } = 600;

    public void Validate()
    {
        if (
            !Enum.IsDefined(this.Profile)
            || this.MaximumExposureFraction is <= 0 or > 1
            || this.MaximumConcentrationFraction is <= 0 or > 1
            || this.RiskToStopFraction is <= 0 or > 1
            || this.AtrMultiplier is < 1 or > 10
            || this.MaximumSpreadFraction is < 0 or > 0.1m
            || this.EstimatedFeeRate is < 0 or > 0.01m
            || this.MaximumDataAgeSeconds is < 10 or > 300
            || this.MaximumSignalAgeSeconds is < 60 or > 900
        )
        {
            throw new ArgumentException("Invalid relative risk policy.");
        }
    }
}
