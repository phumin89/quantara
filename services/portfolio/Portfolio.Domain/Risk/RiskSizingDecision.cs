namespace Quantara.Portfolio.Domain.Risk;

public sealed record RiskSizingDecision(
    bool Accepted,
    string Code,
    decimal? Amount,
    decimal? NotionalThb,
    decimal? AllocationFraction,
    decimal? ProjectedExposureThb,
    decimal? EstimatedDownsideThb,
    decimal? EstimatedCostThb,
    string Explanation
);
