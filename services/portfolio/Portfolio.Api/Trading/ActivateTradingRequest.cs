namespace Quantara.Portfolio.Api;

public sealed record ActivateTradingRequest(
    string AccountKey,
    string RulesHash,
    DateTimeOffset ExpiresAt,
    string Acknowledgement
);
