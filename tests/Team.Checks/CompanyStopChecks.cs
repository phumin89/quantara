using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class CompanyStopChecks
{
    public static async Task Run()
    {
        foreach (var stage in new[] { "preparation", "refresh", "readiness" })
        {
            await StopPendingPreparation(stage);
        }

        Console.WriteLine("Company stop checks passed: preparation, refresh, readiness and restart.");
    }

    private static async Task StopPendingPreparation(string stage)
    {
        var time = new ManualTimeProvider();
        var schedule = new FakeCompanyScheduleStore(time.GetUtcNow());
        var runs = new FakeTeamRunStore();
        var portfolio = new BlockingLoopSnapshotReader(stage);
        var worker = new FakeCodexWorker((_, _, _) => Task.FromResult("fixture report"));
        using var coordinator = new TeamCoordinator(runs, worker, portfolio, time: time);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.Initialization.WaitAsync(TimeSpan.FromSeconds(5));
        using var loop = new CompanyLoop(schedule, runs, coordinator, portfolio, time);
        var tick = loop.Tick(CancellationToken.None);
        Task queuedTick = Task.CompletedTask;
        Task stop = Task.CompletedTask;
        try
        {
            await portfolio.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queuedTick = loop.Tick(CancellationToken.None);
            stop = loop.SetEnabled(false, CancellationToken.None);
            try
            {
                await stop.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException error)
            {
                throw new InvalidOperationException($"Company Stop did not interrupt pending {stage}.", error);
            }

            await Task.WhenAll(tick, queuedTick).WaitAsync(TimeSpan.FromSeconds(5));
            Check(!portfolio.Release.Task.IsCompleted, "Stop finishes before the blocked operation is released");
            Check(portfolio.Entries == 1, "a queued tick cannot restart preparation while Stop is pending");
            Check(!schedule.Schedule.Enabled, "Stop is persisted");
            Check((await loop.Read(CancellationToken.None)).Status == CompanyLoopStatus.Stopped, "Stop is visible");
            Check(
                await runs.Latest(CancellationToken.None) is null && worker.Calls.Count == 0,
                "no run or model call is created"
            );

            using var restarted = new CompanyLoop(schedule, runs, coordinator, portfolio, time);
            time.Advance(TimeSpan.FromHours(2));
            await restarted.Tick(CancellationToken.None);
            Check(await runs.Latest(CancellationToken.None) is null, "the saved Stop survives restart");

            portfolio.Release.TrySetResult();
            await loop.SetEnabled(true, CancellationToken.None);
            await loop.Tick(CancellationToken.None);
            Check(await runs.Latest(CancellationToken.None) is not null, "explicit Resume can prepare a new run");
            await loop.SetEnabled(false, CancellationToken.None);
        }
        finally
        {
            portfolio.Release.TrySetResult();
            await Task.WhenAll(tick, queuedTick, stop).WaitAsync(TimeSpan.FromSeconds(10));
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Company stop check failed: " + message);
        }
    }
}
