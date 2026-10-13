namespace Quantara.Portfolio.Application;

public sealed record AccountEvidenceView(
    string AccountKey,
    DateTimeOffset ObservedAt,
    AccountBalanceEvidence[] Balances,
    AccountEvidenceFee Fee
);
