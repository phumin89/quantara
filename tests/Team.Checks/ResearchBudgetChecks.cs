using Quantara.Team.Application;

namespace Team.Checks;

public static class ResearchBudgetChecks
{
    public static Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 19, 0, 30, 0, TimeSpan.FromHours(7));
        var old = Enumerable
            .Range(0, 32)
            .Select(index => new ResearchBudgetAttempt(now.AddHours(-2).AddMinutes(index), false))
            .ToArray();
        Check(ResearchBudgetRules.Decide(old, false, now).Allowed, "Old claims do not impose a calendar-day limit.");
        var ordinary = new[]
        {
            new ResearchBudgetAttempt(now.AddMinutes(-45), false),
            new ResearchBudgetAttempt(now.AddMinutes(-10), false),
        };
        Check(!ResearchBudgetRules.Decide(ordinary, false, now).Allowed, "Ordinary lane allows two claims per hour.");
        Check(
            ResearchBudgetRules
                .Decide(
                    [
                        new ResearchBudgetAttempt(now.AddHours(-1), false),
                        new ResearchBudgetAttempt(now.AddMinutes(-10), false),
                    ],
                    false,
                    now
                )
                .Allowed,
            "Exact release boundary succeeds."
        );
        Check(ResearchBudgetRules.Decide(ordinary, true, now).Allowed, "Priority lane remains available.");
        var priority = new[] { new ResearchBudgetAttempt(now.AddMinutes(-45), true) };
        Check(
            ResearchBudgetRules.Decide(priority, false, now).Allowed,
            "Ordinary lane remains available after priority saturation."
        );
        ResearchBudgetAttempt[] full = [.. ordinary, .. priority];
        var denied = ResearchBudgetRules.Decide(full, false, now);
        Check(
            !denied.Allowed && denied.Reason == "rolling_hour_limit" && denied.NextAllowedAt == now.AddMinutes(15),
            "Three total claims and exact release are explicit."
        );
        Check(
            ResearchBudgetRules
                .Decide(
                    [
                        new ResearchBudgetAttempt(now.AddMinutes(-59), false),
                        new ResearchBudgetAttempt(now.AddMinutes(-50), false),
                        new ResearchBudgetAttempt(now.AddMinutes(-40), false),
                        new ResearchBudgetAttempt(now.AddMinutes(-30), false),
                    ],
                    false,
                    now
                )
                .NextAllowedAt == now.AddMinutes(20),
            "Overfull retained claims release the required later claim."
        );
        Check(
            ResearchBudgetRules.Decide([new ResearchBudgetAttempt(now.AddMinutes(10), false)], false, now).Reason
                == "future_claim",
            "Future claims defer explicitly."
        );
        Check(
            ResearchBudgetRules.Decide(Array.Empty<ResearchBudgetAttempt>(), false, now).Allowed,
            "An independently scoped empty account can claim."
        );
        Console.WriteLine("Research budget: rolling-hour lanes and future claims passed.");
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
