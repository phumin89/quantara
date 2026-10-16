namespace Quantara.Portfolio.Application;

public sealed record AccountEvidenceView(
    string AccountKey,
    DateTimeOffset ObservedAt,
    AccountBalanceEvidence[] Balances,
    AccountEvidenceFee Fee
)
{
    public string SizingProfile { get; init; } = "disabled";

    public string? RiskPolicyHash { get; init; }
}
