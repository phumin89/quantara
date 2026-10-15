using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class CompanyLoopChecks
{
    public static async Task Run()
    {
        var time = new ManualTimeProvider();
        var store = new FakeCompanyScheduleStore(time.GetUtcNow());
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        var portfolio = new LoopSnapshotReader();
        using var loop = new CompanyLoop(store, runs, coordinator, portfolio, time);
        await Task.WhenAll(loop.Tick(CancellationToken.None), loop.Tick(CancellationToken.None));
        CompanyLoopChecks.Check(coordinator.Objectives.Count == 1, "first automatic work/no overlap");
        CompanyLoopChecks.Check(portfolio.Refreshes == 1, "refresh read-only portfolio before analysis");
        CompanyLoopChecks.Check(store.Schedule.NextRunAt == time.GetUtcNow().AddHours(1), "durable hour reservation");
        var first = await runs.Latest(CancellationToken.None) ?? throw new InvalidOperationException("Run missing");
        await runs.Save(
            first with
            {
                Status = TeamRunStatus.Failed,
                Error = "Codex worker failed [worker_rate_limited].",
            },
            CancellationToken.None
        );
        var failedOverview = await loop.Read(CancellationToken.None);
        CompanyLoopChecks.Check(
            failedOverview.Status == CompanyLoopStatus.Error
                && failedOverview.Enabled
                && failedOverview.Error?.Contains("worker_rate_limited", StringComparison.Ordinal) == true
                && failedOverview.NextRunAt == store.Schedule.NextRunAt,
            "Failed analysis is visible without stopping or accelerating the schedule."
        );
        await runs.Save(
            first with
            {
                Status = TeamRunStatus.Completed,
                Steps = [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, "Retained review", null)],
            },
            CancellationToken.None
        );
        time.Advance(TimeSpan.FromMinutes(59));
        await loop.Tick(CancellationToken.None);
        await loop.SetEnabled(true, CancellationToken.None);
        await loop.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(coordinator.Objectives.Count == 1, "no early work/idempotent resume");
        time.Advance(TimeSpan.FromMinutes(1));
        await loop.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(
            coordinator.Objectives.Count == 2
                && coordinator.Objectives[1].Contains(first.Id.ToString("D"), StringComparison.Ordinal),
            "next round/previous context"
        );
        await loop.SetEnabled(false, CancellationToken.None);
        CompanyLoopChecks.Check(
            (await runs.Latest(CancellationToken.None))?.Status == TeamRunStatus.Cancelled,
            "stop cancels active task"
        );
        using var restarted = new CompanyLoop(store, runs, coordinator, portfolio, time);
        time.Advance(TimeSpan.FromDays(1));
        await restarted.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(
            coordinator.Objectives.Count == 2
                && (await restarted.Read(CancellationToken.None)).Status == CompanyLoopStatus.Stopped,
            "pause survives restart"
        );
        await restarted.SetEnabled(true, CancellationToken.None);
        coordinator.IsConfigured = false;
        await restarted.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(
            coordinator.Objectives.Count == 2
                && (await restarted.Read(CancellationToken.None)).Status == CompanyLoopStatus.WaitingForCodex,
            "missing login prevents work"
        );
        coordinator.IsConfigured = true;
        portfolio.Available = false;
        await restarted.Tick(CancellationToken.None);
        var refreshes = portfolio.Refreshes;
        await restarted.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(portfolio.Refreshes == refreshes, "no repeated import while waiting");
        CompanyLoopChecks.Check(
            coordinator.Objectives.Count == 2
                && (await restarted.Read(CancellationToken.None)).Status == CompanyLoopStatus.WaitingForPortfolio,
            "missing portfolio prevents work"
        );
        portfolio.Available = true;
        coordinator.FailStart = true;
        await restarted.Tick(CancellationToken.None);
        using var uncertain = new CompanyLoop(store, runs, coordinator, portfolio, time);
        await uncertain.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(coordinator.Objectives.Count == 3, "uncertain start does not hot retry after restart");
        time.Advance(TimeSpan.FromDays(2));
        coordinator.FailStart = false;
        await uncertain.Tick(CancellationToken.None);
        await uncertain.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(coordinator.Objectives.Count == 4, "one overdue round/no backlog catch-up");
        store.FailSave = true;
        try
        {
            await uncertain.SetEnabled(false, CancellationToken.None);
            throw new InvalidOperationException("Stop persistence failure was hidden");
        }
        catch (InvalidOperationException error) when (error.Message == "Storage failure fixture") { }

        await uncertain.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(
            coordinator.Objectives.Count == 4
                && (await runs.Latest(CancellationToken.None))?.Status == TeamRunStatus.Cancelled,
            "storage failure still cancels work and stops locally"
        );
        CompanyLoopChecks.Check(
            (await uncertain.Read(CancellationToken.None)).Status == CompanyLoopStatus.Error,
            "unconfirmed stop is visible"
        );
        store.FailSave = false;
        await uncertain.SetEnabled(false, CancellationToken.None);
        CompanyLoopChecks.Check(!store.Schedule.Enabled, "retry stop persists disabled state");

        var overnightTime = new ManualTimeProvider();
        overnightTime.Advance(TimeSpan.FromDays(3) + TimeSpan.FromHours(16));
        var overnightStore = new FakeCompanyScheduleStore(overnightTime.GetUtcNow());
        var overnightRuns = new FakeTeamRunStore();
        var overnightCoordinator = new LoopCoordinator(overnightRuns, overnightTime);
        using var overnight = new CompanyLoop(
            overnightStore,
            overnightRuns,
            overnightCoordinator,
            new LoopSnapshotReader(),
            overnightTime
        );
        await overnight.Tick(CancellationToken.None);
        CompanyLoopChecks.Check(overnightCoordinator.Objectives.Count == 1, "late Friday round starts");
        CompanyLoopChecks.Check(
            overnightCoordinator.Objectives[0].Contains("Trading day (Bangkok): 2026-10-16", StringComparison.Ordinal),
            "late Friday Bangkok trading day"
        );
        var overnightFirst =
            await overnightRuns.Latest(CancellationToken.None)
            ?? throw new InvalidOperationException("Overnight run missing");
        await overnightRuns.Save(overnightFirst with { Status = TeamRunStatus.Completed }, CancellationToken.None);
        overnightTime.Advance(TimeSpan.FromHours(1));
        await Task.WhenAll(overnight.Tick(CancellationToken.None), overnight.Tick(CancellationToken.None));
        CompanyLoopChecks.Check(overnightCoordinator.Objectives.Count == 2, "Saturday midnight next round/no overlap");
        CompanyLoopChecks.Check(
            overnightCoordinator.Objectives[1].Contains("Trading day (Bangkok): 2026-10-17", StringComparison.Ordinal),
            "Saturday Bangkok trading day"
        );
        CompanyLoopChecks.Check(
            overnightStore.Schedule.NextRunAt == overnightTime.GetUtcNow().AddHours(1),
            "overnight target and cadence unchanged"
        );
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Company loop check failed: {name}");
        }
    }
}
