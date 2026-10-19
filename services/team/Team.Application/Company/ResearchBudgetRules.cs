namespace Quantara.Team.Application;

public static class ResearchBudgetRules
{
    public static ResearchBudgetDecision Decide(
        IReadOnlyList<ResearchBudgetAttempt> claims,
        bool positionReview,
        DateTimeOffset now
    )
    {
        var future = claims.Where(claim => claim.ClaimedAt > now).OrderBy(claim => claim.ClaimedAt).FirstOrDefault();
        if (future is not null)
        {
            return new(false, future.ClaimedAt, "future_claim");
        }

        var recent = claims.Where(claim => claim.ClaimedAt > now.AddHours(-1) && claim.ClaimedAt <= now).ToArray();
        var ordered = recent.OrderBy(claim => claim.ClaimedAt).ToArray();
        var totalRelease =
            ordered.Length >= 3 ? ordered[ordered.Length - 3].ClaimedAt.AddHours(1) : (DateTimeOffset?)null;
        var lane = recent.Where(claim => claim.PositionReview == positionReview).ToArray();
        var orderedLane = lane.OrderBy(claim => claim.ClaimedAt).ToArray();
        var laneLimit = positionReview ? 1 : 2;
        var laneRelease =
            orderedLane.Length >= laneLimit
                ? orderedLane[orderedLane.Length - laneLimit].ClaimedAt.AddHours(1)
                : (DateTimeOffset?)null;
        DateTimeOffset? next = totalRelease;
        if (laneRelease is not null && (next is null || laneRelease > next))
        {
            next = laneRelease;
        }

        if (next is not null)
        {
            var reason =
                totalRelease is not null ? "rolling_hour_limit"
                : positionReview ? "position_review_limit"
                : "ordinary_research_limit";
            return new(false, next, reason);
        }

        return new(true, null, "claimed");
    }

    public static ResearchBudgetDecision Decide(
        IReadOnlyList<DateTimeOffset> claims,
        bool positionReview,
        DateTimeOffset now
    ) => Decide(claims.Select(claim => new ResearchBudgetAttempt(claim, false)).ToArray(), positionReview, now);
}
