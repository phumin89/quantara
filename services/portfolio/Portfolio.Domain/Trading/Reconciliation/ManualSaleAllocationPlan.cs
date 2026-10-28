namespace Quantara.Portfolio.Domain;

public sealed record ManualSaleAllocationPlan(
    ManualSaleLot Lot,
    IReadOnlyList<ManualSaleAllocation> Journal,
    decimal AllocatedSoldQuantity,
    decimal UnallocatedHistoricalQuantity,
    decimal? VerifiedCreditedRemainder,
    decimal UnresolvedRecordedCostThb,
    decimal? AllocatedCostThb,
    decimal? RealizedNetProfitThb
);
