namespace Quantara.Team.Application;

public static class ResearchBudgetRules
{
    public static ResearchBudgetDecision Decide(
        IReadOnlyList<DateTimeOffset> claims,
        bool hasHeldPosition,
        DateTimeOffset now
    )
    {
        var bangkok = now.ToOffset(TimeSpan.FromHours(7));
        var dayStart = new DateTimeOffset(bangkok.Date, bangkok.Offset);
        var hourly = claims.Where(at => at > now.AddHours(-1)).Order().ToArray();
        DateTimeOffset? next = hourly.Length >= 2 ? hourly[0].AddHours(1) : null;
        var daily = claims.Count(at => at >= dayStart);
        if (daily >= (hasHeldPosition ? 32 : 24))
        {
            next = dayStart.AddDays(1);
        }

        if (claims.Any(at => at > now.AddSeconds(5)))
        {
            next = claims.Max().AddHours(1);
        }

        return next is { } at
            ? new(false, at, daily >= (hasHeldPosition ? 32 : 24) ? "daily_round_limit" : "rolling_hour_limit")
            : new(true, null, "claimed");
    }
}
