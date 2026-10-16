using System.Globalization;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record RiskCapitalView(
    string AccountKey,
    DateTimeOffset ObservedAt,
    string? SpendableThb,
    string? EquityThb,
    string? ExposureThb,
    string? ReservedThb,
    bool HasUncertainOrders,
    string? Blocker
)
{
    public static RiskCapitalView From(CapitalSnapshot value) =>
        new(
            value.AccountKey,
            value.ObservedAt,
            Number(value.SpendableThb),
            Number(value.EquityThb),
            Number(value.ExposureThb),
            Number(value.ReservedThb),
            value.HasUncertainOrders,
            value.Blocker
        );

    private static string? Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
}
