using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public static class TradingPolicyPersistence
{
    public static void Validate(TradingState previous, TradingState next)
    {
        if (
            next.PolicyChanges.Count < previous.PolicyChanges.Count
            || next.PolicyChanges.Count > previous.PolicyChanges.Count + 1
            || previous
                .PolicyChanges.Where(
                    (change, index) =>
                        JsonSerializer.Serialize(change) != JsonSerializer.Serialize(next.PolicyChanges[index])
                )
                .Any()
        )
        {
            throw new InvalidDataException("Applied policy history must be retained unchanged.");
        }

        if (next.PolicyChanges.Count == previous.PolicyChanges.Count)
        {
            return;
        }

        var added = next.PolicyChanges[^1];
        added.Candidate.Validate(added.AppliedAt);
        if (
            !TradingAuthority.Valid(previous, added.Candidate.AccountKey, added.AppliedAt)
            || !TradingAuthority.Valid(next, added.Candidate.AccountKey, added.AppliedAt)
            || previous.Mandate != next.Mandate
            || previous.AiPolicy?.MandateId != added.Authorization.MandateId
            || added.Evaluation.BaselineRulesHash != previous.RulesHash
            || added.Evaluation.CandidateRulesHash != next.RulesHash
            || added.Evaluation.DatasetHash.Length != 64
            || added.Evaluation.ObservedAt > added.AppliedAt
            || added.AppliedAt - added.Evaluation.ObservedAt > TimeSpan.FromSeconds(next.Rules.MaxDataAgeSeconds)
            || previous.Live.Orders.Concat(previous.Paper.Orders).Any(TradingLedgerAccounting.Active)
            || previous.Live.Positions.Concat(previous.Paper.Positions).Any(position => position.Quantity > 0)
            || previous.PolicyChanges.Any(change => change.Candidate.RunId == added.Candidate.RunId)
        )
        {
            throw new InvalidDataException(
                "A policy change requires an unchanged owner authorization, fresh evaluation and flat accounting state."
            );
        }
    }
}
