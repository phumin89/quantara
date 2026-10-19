using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ContinuousScheduleChecks
{
    private static readonly string[] activeRoles =
    [
        "positions",
        "intraday",
        "news",
        "swing",
        "quant",
        "ceo",
        "reviewer",
    ];

    public static async Task Run()
    {
        var time = new ManualTimeProvider();
        var store = new FakeTeamRunStore();
        var budget = new InMemoryResearchBudget();
        var worker = new FakeCodexWorker((role, _, _) => Task.FromResult(new ResearchReport(role + " ok", null)));
        var snapshot = new ScheduleSnapshotReader(time);
        var news = new FakeNewsReader("{\"events\":[{\"eventId\":\"schedule\",\"revision\":1}]}");
        var completed = 0;
        for (var tick = 0; tick < 120; tick++)
        {
            using var coordinator = new TeamCoordinator(
                store,
                worker,
                snapshot,
                account: new FakeCurrentAccountReader(new string('a', 64)),
                news: news,
                budget: budget,
                time: time
            );
            await coordinator.StartAsync(CancellationToken.None);
            await coordinator.Initialization;
            try
            {
                var started = await coordinator.StartAutomatic(
                    "continuous research",
                    CancellationToken.None,
                    marketTriggered: tick % 32 == 0
                );
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var run = await store.WaitFor(
                    started.Id,
                    value => value.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
                    timeout.Token
                );
                Check(run.Status == TeamRunStatus.Completed, "Scheduled work completes: " + run.Error);
                completed++;
            }
            catch (ResearchBudgetDeferredException) { }

            await coordinator.StopAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(15));
        }

        Check(completed >= 30, "At least one scheduled work item completed per simulated hour.");
        Check(budget.Claims.Count == completed, "Every completed schedule tick reserved one durable claim.");
        var startHour = time.GetUtcNow().AddHours(-30).Ticks / TimeSpan.TicksPerHour;
        var expectedHours = Enumerable.Range(0, 30).Select(offset => startHour + offset).ToHashSet();
        var actualHours = budget.Claims.Select(claim => claim.ClaimedAt.Ticks / TimeSpan.TicksPerHour).ToHashSet();
        Check(expectedHours.SetEquals(actualHours), "Every elapsed hour has completed work.");
        foreach (var claim in budget.Claims.OrderBy(claim => claim.ClaimedAt))
        {
            var prior = budget.Claims.Where(item =>
                item.ClaimedAt > claim.ClaimedAt.AddHours(-1) && item.ClaimedAt < claim.ClaimedAt
            );
            Check(
                prior.Count() < 3
                    && (
                        claim.PositionReview
                            ? !prior.Any(item => item.PositionReview)
                            : prior.Count(item => !item.PositionReview) < 2
                    ),
                "Rolling hourly lanes remain bounded."
            );
        }

        Check(
            activeRoles.All(role => worker.Calls.Any(call => call.Role == role)),
            "All active roles execute during the simulation."
        );
        Console.WriteLine("Continuous schedule: 30-hour restart and rolling budget checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
