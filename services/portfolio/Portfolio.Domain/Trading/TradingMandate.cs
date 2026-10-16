namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingMandate(
    Guid Id,
    string AccountKey,
    string RulesHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt
);
