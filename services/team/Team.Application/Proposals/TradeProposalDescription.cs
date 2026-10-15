using System.Globalization;

namespace Quantara.Team.Application;

public static class TradeProposalDescription
{
    public static string Format(TradeProposal proposal)
    {
        var reason = proposal.Reason.Trim();
        if (proposal.EntryPlan is not { } plan)
        {
            return reason;
        }

        var reviewAt = proposal.EvidenceAt.AddMinutes(plan.ReviewAfterMinutes).ToOffset(TimeSpan.FromHours(7));
        var description =
            reason
            + "\nTarget (plan): THB "
            + plan.TargetPrice
            + "\nInvalidation (plan): THB "
            + plan.InvalidationPrice
            + "\nReview by: "
            + reviewAt.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)
            + ". Exit/cancel requires review; not automatic.";
        return description.Length <= 500
            ? description
            : throw new InvalidOperationException("Entry plan exceeds the human review message budget.");
    }
}
