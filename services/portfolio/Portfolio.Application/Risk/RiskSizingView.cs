using System.Globalization;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record RiskSizingView(
    string PolicyHash,
    DateTimeOffset ObservedAt,
    string EquityThb,
    string AllocationFraction,
    string EstimatedDownsideThb,
    string EstimatedCostThb,
    string ProjectedExposureThb,
    string Code,
    string Explanation
)
{
    public static RiskSizingView From(RiskSizingSnapshot value) =>
        new(
            value.PolicyHash,
            value.Capital.ObservedAt,
            Number(value.Capital.EquityThb),
            Number(value.Decision.AllocationFraction),
            Number(value.Decision.EstimatedDownsideThb),
            Number(value.Decision.EstimatedCostThb),
            Number(value.Decision.ProjectedExposureThb),
            value.Decision.Code,
            value.Decision.Explanation
        );

    private static string Number(decimal? value) =>
        (value ?? throw new InvalidDataException("Stored sizing is incomplete.")).ToString(
            CultureInfo.InvariantCulture
        );
}
