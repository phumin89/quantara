using Quantara.Team.Application;

namespace Team.Checks;

public static class ResearchBudgetChecks
{
    public static Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 19, 0, 30, 0, TimeSpan.FromHours(7));
        var hourly = new[] { now.AddMinutes(-45), now.AddMinutes(-10) };
        Check(!ResearchBudgetRules.Decide(hourly, true, now).Allowed, "Rolling-hour claims cross Bangkok midnight.");
        Check(
            ResearchBudgetRules.Decide(hourly, true, now).NextAllowedAt == now.AddMinutes(15),
            "Exact hourly next attempt is explicit."
        );
        var endOfDay = now.Date.AddHours(23);
        var late = new DateTimeOffset(endOfDay, now.Offset);
        var daily = Enumerable.Range(0, 24).Select(index => now.AddMinutes(index * 40)).ToArray();
        Check(!ResearchBudgetRules.Decide(daily, false, late).Allowed, "Ordinary rounds reserve eight slots.");
        Check(ResearchBudgetRules.Decide(daily, true, late).Allowed, "Position reviews may use reserved slots.");
        var full = Enumerable.Range(0, 32).Select(index => now.AddMinutes(index * 30)).ToArray();
        Check(!ResearchBudgetRules.Decide(full, true, late).Allowed, "No more than 32 rounds per Bangkok day.");
        Check(
            !ResearchBudgetRules.Decide(full.ToArray(), true, late).Allowed,
            "Reloaded durable claims are not refunded."
        );
        Check(ResearchBudgetRules.Decide([], false, now).Allowed, "An independently scoped empty account can claim.");
        Console.WriteLine(
            "Research budget: rolling hour, Bangkok day, reserved position reviews and reloaded claims passed."
        );
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
