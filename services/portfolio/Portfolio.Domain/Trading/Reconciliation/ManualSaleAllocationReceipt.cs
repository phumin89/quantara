namespace Quantara.Portfolio.Domain;

public sealed record ManualSaleAllocationReceipt(
    ManualSaleAllocation Allocation,
    string ReviewReference,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ReviewExpiresAt = null
);
