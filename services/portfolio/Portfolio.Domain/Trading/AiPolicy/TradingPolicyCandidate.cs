namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingPolicyCandidate(
    Guid RunId,
    string AccountKey,
    string BaselineRulesHash,
    DateTimeOffset AsOf,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> EvidenceIds,
    string Reason,
    TradingPolicyParameters Parameters
)
{
    public void Validate(DateTimeOffset now)
    {
        if (
            this.RunId == Guid.Empty
            || string.IsNullOrWhiteSpace(this.AccountKey)
            || this.AccountKey.Length > 128
            || this.BaselineRulesHash is not { Length: 64 }
            || this.BaselineRulesHash.Any(character => character is not (>= 'a' and <= 'f' or >= '0' and <= '9'))
            || this.AsOf > now
            || now - this.AsOf > TimeSpan.FromMinutes(30)
            || this.ExpiresAt <= now
            || this.ExpiresAt > this.AsOf.AddMinutes(30)
            || this.EvidenceIds is not { Count: >= 1 and <= 16 }
            || this.EvidenceIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 256)
            || this.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != this.EvidenceIds.Count
            || string.IsNullOrWhiteSpace(this.Reason)
            || this.Reason.Length > 1600
            || this.Parameters is null
        )
        {
            throw new ArgumentException(
                "The AI candidate must identify fresh reviewed evidence and its exact baseline."
            );
        }
    }
}
