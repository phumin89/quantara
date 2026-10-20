namespace Quantara.Portfolio.Domain.Risk;

public sealed record CapitalSnapshot(
    string AccountKey,
    DateTimeOffset ObservedAt,
    decimal? SpendableThb,
    decimal? EquityThb,
    decimal? ExposureThb,
    decimal? ReservedThb,
    IReadOnlyList<CapitalAsset> Assets,
    bool HasUncertainOrders,
    string? Blocker
)
{
    public CapitalSource Source { get; init; }
}
