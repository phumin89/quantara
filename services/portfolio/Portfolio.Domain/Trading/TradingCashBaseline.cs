namespace Quantara.Portfolio.Domain.Trading;

// A reviewed present balance, never a historical settlement, contribution or profit.
public sealed record TradingCashBaseline(
    string AccountKey,
    decimal PreviousCashThb,
    decimal CashThb,
    decimal AvailableThb,
    decimal RetainedFundingThb,
    DateTimeOffset BrokerObservedAt,
    DateTimeOffset Cutoff,
    DateTimeOffset RecordedAt,
    DateTimeOffset ReviewExpiresAt,
    string RulesHash,
    string EvidenceHash,
    string MaterialBinding,
    string ReviewReference,
    string[] CoveredExternalSales
);
