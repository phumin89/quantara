namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingPolicyAuthorization(
    Guid MandateId,
    string AccountKey,
    string OriginalRulesHash,
    string EffectiveRulesHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt
)
{
    public decimal CapitalCeilingFraction { get; init; } = 1m;

    public DateTimeOffset? RevokedAt { get; init; }

    public DateTimeOffset? LastCheckedAt { get; init; }

    public string Status { get; init; } = "waiting";

    public string Reason { get; init; } = "Waiting for the research team's reviewed settings.";
}
