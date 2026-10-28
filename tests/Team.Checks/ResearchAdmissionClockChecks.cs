using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchAdmissionClockChecks
{
    public static async Task Run()
    {
        await BudgetIsolation();
        var time = new ManualTimeProvider();
        var startedAt = time.GetUtcNow();
        var collectedAt = startedAt.AddSeconds(2);
        var store = new FakeTeamRunStore();
        var budget = new InMemoryResearchBudget();
        var worker = new FakeCodexWorker((_, _, _) => Task.FromResult("In-process research fixture"));
        var newsReads = 0;
        var news = new AdvancingNewsReader(
            "{\"events\":[]}",
            () =>
            {
                if (Interlocked.Increment(ref newsReads) == 1)
                {
                    time.Advance(TimeSpan.FromSeconds(2));
                }
            }
        );
        using var coordinator = new TeamCoordinator(
            store,
            worker,
            new ScheduleSnapshotReader(time),
            account: new FakeCurrentAccountReader(new string('a', 64)),
            news: news,
            budget: budget,
            time: time
        );
        try
        {
            await coordinator.StartAsync(CancellationToken.None);
            await coordinator.Initialization.WaitAsync(TimeSpan.FromSeconds(5));

            var run = await coordinator.StartAutomatic("Research admission clock fixture", CancellationToken.None);
            var task = run.Task ?? throw new InvalidOperationException("Research admission task is missing.");

            Check(time.GetUtcNow() == collectedAt, "news collection advances the fake clock once");
            Check(run.Status == TeamRunStatus.Queued, "admission returns the original queued record");
            Check(run.CreatedAt == collectedAt, "queued creation time follows news collection");
            Check(run.UpdatedAt == collectedAt, "queued update time follows news collection");
            Check(task.Specialist == TeamRole.Positions, "held fixture selects a position review");
            Check(task.EvidenceAt == collectedAt, "task evidence time follows news collection");
            Check(task.ExpiresAt == collectedAt.AddMinutes(20), "task lifetime starts after collection");
            Check(budget.Claims.Count == 1, "admission makes one in-process budget claim");
            Check(budget.Claims[0].ClaimedAt == collectedAt, "budget uses the post-collection clock");
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    private static async Task BudgetIsolation()
    {
        var now = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        var accountA = new string('a', 64);
        var accountB = new string('b', 64);
        var budget = new InMemoryResearchBudget();
        var emptySnapshot = budget.Claims;
        foreach (var account in new[] { accountA, accountB })
        {
            Check(
                (await budget.TryClaim(account, true, now, CancellationToken.None)).Allowed,
                "each account has its own position-review allowance"
            );
            var denied = await budget.TryClaim(account, true, now.AddMinutes(1), CancellationToken.None);
            Check(
                !denied.Allowed && denied.Reason == "position_review_limit" && denied.NextAllowedAt == now.AddHours(1),
                "position-review lane refuses a second claim until its exact hour boundary"
            );
        }

        Check(emptySnapshot.Count == 0, "aggregate Claims is a retained snapshot");
        foreach (var account in new[] { accountA, accountB })
        {
            for (var minute = 1; minute <= 2; minute++)
            {
                Check(
                    (await budget.TryClaim(account, false, now.AddMinutes(minute), CancellationToken.None)).Allowed,
                    "each account retains two ordinary claims after its position review"
                );
            }

            var denied = await budget.TryClaim(account, false, now.AddMinutes(3), CancellationToken.None);
            Check(
                !denied.Allowed && denied.Reason == "rolling_hour_limit" && denied.NextAllowedAt == now.AddMinutes(61),
                "a full account waits for both its rolling total and ordinary lane"
            );
        }

        var beforeBoundary = await budget.TryClaim(
            accountA,
            true,
            now.AddHours(1).AddTicks(-1),
            CancellationToken.None
        );
        Check(
            !beforeBoundary.Allowed && beforeBoundary.NextAllowedAt == now.AddHours(1),
            "the position claim remains counted until the exact rolling-hour boundary"
        );
        Check(
            (await budget.TryClaim(accountA, true, now.AddHours(1), CancellationToken.None)).Allowed,
            "the expired position claim releases exactly at one hour"
        );
        var ordinaryStillFull = await budget.TryClaim(accountA, false, now.AddHours(1), CancellationToken.None);
        Check(
            !ordinaryStillFull.Allowed
                && ordinaryStillFull.Reason == "rolling_hour_limit"
                && ordinaryStillFull.NextAllowedAt == now.AddMinutes(61),
            "releasing a position claim does not release the later ordinary claims"
        );
        Check(
            (await budget.TryClaim(accountA, false, now.AddMinutes(61), CancellationToken.None)).Allowed,
            "the first ordinary claim releases at its own exact hour boundary"
        );

        var ordinary = new InMemoryResearchBudget();
        foreach (var account in new[] { accountA, accountB })
        {
            Check(
                (await ordinary.TryClaim(account, false, now, CancellationToken.None)).Allowed
                    && (await ordinary.TryClaim(account, false, now.AddMinutes(1), CancellationToken.None)).Allowed,
                "each account has two independent ordinary-research claims"
            );
            var denied = await ordinary.TryClaim(account, false, now.AddMinutes(2), CancellationToken.None);
            Check(
                !denied.Allowed
                    && denied.Reason == "ordinary_research_limit"
                    && denied.NextAllowedAt == now.AddHours(1),
                "the ordinary lane cap is enforced before the total cap"
            );
            Check(
                (await ordinary.TryClaim(account, true, now.AddMinutes(2), CancellationToken.None)).Allowed,
                "ordinary-research exhaustion preserves the reserved position lane"
            );
        }

        Check(ordinary.Claims.Count == 6, "aggregate Claims retains allowed attempts from both accounts");
        var retained = budget.Claims;
        Check(retained.Count == 8, "rejected claims do not consume the aggregate allowance");
        foreach (var invalid in new[] { "", new string('a', 63), new string('a', 65), new string('g', 64) })
        {
            try
            {
                await budget.TryClaim(invalid, false, now, CancellationToken.None);
                throw new InvalidOperationException("A malformed research-budget account was accepted.");
            }
            catch (InvalidDataException error) when (error.Message == "Invalid research budget account.") { }

            Check(budget.Claims.SequenceEqual(retained), "invalid account claims do not mutate aggregate Claims");
        }

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            await budget.TryClaim(new string('c', 64), false, now, cancellation.Token);
            throw new InvalidOperationException("A cancelled research-budget claim was accepted.");
        }
        catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }

        Check(budget.Claims.SequenceEqual(retained), "cancelled claims do not mutate aggregate Claims");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Research admission clock check failed: {name}");
        }
    }
}
