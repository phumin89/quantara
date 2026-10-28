namespace Quantara.Portfolio.Application;

public sealed record CashBaselineReviewSnapshot(
    string AccountKey,
    decimal CashThb,
    decimal AvailableThb,
    DateTimeOffset BrokerObservedAt,
    string EvidenceHash,
    string MaterialBinding,
    long TradingRevision,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt
);
