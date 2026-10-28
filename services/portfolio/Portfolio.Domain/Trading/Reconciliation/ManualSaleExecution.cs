namespace Quantara.Portfolio.Domain;

// Supplied only from a verified broker execution, never from an order's requested amount or wallet delta.
public sealed record ManualSaleExecution(
    string AccountKey,
    string ExecutionId,
    string OrderId,
    string Symbol,
    TradeSide Side,
    decimal Quantity,
    DateTimeOffset OccurredAt,
    DateTimeOffset ObservedAt,
    string EvidenceReference
);
