namespace Quantara.Team.Domain;

public sealed record PolicyCandidate(
    Guid RunId,
    string AccountKey,
    string BaselineRulesHash,
    DateTimeOffset AsOf,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> EvidenceIds,
    string Reason,
    PolicyCandidateParameters Parameters
);
