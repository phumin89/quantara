using System.Globalization;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record RiskPerformanceView(
    DateTimeOffset From,
    DateTimeOffset To,
    string? NetEquityChangeThb,
    string? TimeWeightedReturn,
    string Explanation
)
{
    public static RiskPerformanceView FromPerformance(RiskPerformance value) =>
        new(
            value.From,
            value.To,
            value.NetEquityChangeThb?.ToString(CultureInfo.InvariantCulture),
            value.TimeWeightedReturn?.ToString(CultureInfo.InvariantCulture),
            value.Explanation
        );
}
