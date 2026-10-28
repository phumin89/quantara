namespace Quantara.Portfolio.Domain;

// Only an explicit broker cash-credit record can supply these values; price times quantity is not settlement.
public sealed record ManualSaleSettlement(
    string AccountKey,
    string Symbol,
    string ExecutionId,
    decimal NetThb,
    string EvidenceReference
);
