using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingPolicyAuthority
{
    public static bool Matches(TradingState state)
    {
        if (
            state.AiPolicy is not { RevokedAt: null, CapitalCeilingFraction: 1m } authority
            || state.Mandate is not { RevokedAt: null } mandate
            || authority.MandateId != mandate.Id
            || authority.AccountKey != mandate.AccountKey
            || authority.CreatedAt != mandate.CreatedAt
            || authority.ExpiresAt != mandate.ExpiresAt
            || authority.OriginalRulesHash != mandate.RulesHash
            || authority.EffectiveRulesHash != state.RulesHash
            || state.Rules.Version != "adaptive-intraday-v2"
        )
        {
            return false;
        }

        var expected = authority.OriginalRulesHash;
        foreach (var change in state.PolicyChanges.Where(change => change.Authorization.MandateId == mandate.Id))
        {
            if (
                change.Authorization.OriginalRulesHash != authority.OriginalRulesHash
                || change.Authorization.AccountKey != authority.AccountKey
                || change.Authorization.CreatedAt != authority.CreatedAt
                || change.Authorization.ExpiresAt != authority.ExpiresAt
                || change.Authorization.CapitalCeilingFraction != 1m
                || change.Authorization.EffectiveRulesHash != expected
                || change.Candidate.AccountKey != authority.AccountKey
                || change.Candidate.BaselineRulesHash != expected
                || TradingRuleHash.Compute(change.PreviousRules) != expected
                || !change.Evaluation.Accepted
                || change.Evaluation.BaselineRulesHash != expected
                || change.Evaluation.CandidateRulesHash != TradingRuleHash.Compute(change.AppliedRules)
                || TradingRuleHash.Compute(change.Candidate.Parameters.Apply(change.PreviousRules))
                    != TradingRuleHash.Compute(change.AppliedRules)
            )
            {
                return false;
            }

            expected = TradingRuleHash.Compute(change.AppliedRules);
        }

        return expected == state.RulesHash;
    }

    public static TradingPolicyAuthorization? Revoke(TradingState state, DateTimeOffset now) =>
        state.AiPolicy is not { } authority
            ? null
            : authority with
            {
                RevokedAt = authority.RevokedAt ?? now,
                Status = "stopped",
                Reason = "AI strategy changes are stopped.",
            };
}
