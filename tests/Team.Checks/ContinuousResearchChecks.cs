using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ContinuousResearchChecks
{
    public static async Task Run()
    {
        Routing();
        await FocusedHandoff();
        await DeferredRestart();
        await LegacyCooldownRestart();
        await EvidenceWaitPreservesHistoricalFailure();
        Console.WriteLine(
            "Continuous research: focused routing, role provenance, selective handoff and durable deferral passed."
        );
    }

    private static void Routing()
    {
        var now = new DateTimeOffset(2026, 10, 20, 0, 10, 0, TimeSpan.FromHours(7));
        using var document = JsonDocument.Parse(
            """
            {"accountKey":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
             "positions":[],"marketResearch":{"historicalCandles":{"candles":[[1,2,3,4,5]]}},
             "tradingPerformance":{"evaluation":{"metrics":[{"insufficientSamples":true}]}}}
            """
        );
        const string news = """{"events":[{"eventId":"news1","revision":1}]}""";
        var initial = TeamResearchRouter.Candidates(document.RootElement, news, [], now, false);
        Check(initial[0].Specialist == TeamRole.Intraday, "Fresh intraday work starts before routine summaries.");
        var priorIntraday = new TeamRun(
            Guid.NewGuid(),
            "fixture",
            TeamRunStatus.Completed,
            now.AddMinutes(-20),
            now.AddMinutes(-19),
            [],
            null,
            new string('a', 64),
            Task: initial[0]
        );
        Check(
            TeamResearchRouter.Candidates(document.RootElement, news, [priorIntraday], now, true)[0].Specialist
                == TeamRole.News,
            "Repeated market alerts cannot starve a due news or background specialist."
        );
        Check(
            initial
                .Select(task => task.Specialist)
                .ToHashSet()
                .SetEquals([TeamRole.Intraday, TeamRole.News, TeamRole.Swing, TeamRole.Quant, TeamRole.Ceo]),
            "Evidence routes only useful specialties."
        );
        var history = initial
            .Select(task => new TeamRun(
                Guid.NewGuid(),
                "fixture",
                TeamRunStatus.Failed,
                now.AddMinutes(-5),
                now.AddMinutes(-4),
                [],
                "provider unavailable",
                new string('a', 64),
                Task: task
            ))
            .ToArray();
        Check(
            TeamResearchRouter.Candidates(document.RootElement, news, history, now, false).Count == 0,
            "Started/failed attempts retain cooldown; identical news is not hot retried."
        );
        Check(
            TeamResearchRouter.Candidates(document.RootElement, news, history, now.AddHours(1), false)[0].Specialist
                == TeamRole.Intraday,
            "Research continues after midnight without a daily quota reset."
        );
        Check(
            TeamResearchRouter
                .Candidates(
                    document.RootElement,
                    news,
                    history.Select(run => run with { AccountKey = new string('b', 64) }).ToArray(),
                    now,
                    false
                )
                .Count == initial.Count,
            "Other accounts never consume role cadence."
        );
        var scoped = TeamResearchScope.Project(TeamRole.News, document.RootElement.GetRawText());
        Check(
            !scoped.Contains("historicalCandles", StringComparison.Ordinal)
                && !scoped.Contains("evaluation", StringComparison.Ordinal),
            "News verifier does not receive unrelated candle/study payloads."
        );
    }

    private static async Task FocusedHandoff()
    {
        var store = new FakeTeamRunStore();
        var worker = new FakeCodexWorker(
            (string role, IReadOnlyList<(string Role, string Output)> _, CancellationToken _) =>
                Task.FromResult(new ResearchReport(role + " output", null, Usage: new ResearchUsage(20, 5, 4)))
        );
        using var coordinator = new TeamCoordinator(
            store,
            worker,
            new FakePortfolioSnapshotReader(),
            account: new FakeCurrentAccountReader(new string('a', 64))
        );
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.Initialization;
        try
        {
            var started = await coordinator.StartAutomatic("Focused read-only research", CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var completed = await store.WaitFor(
                started.Id,
                run => run.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
                timeout.Token
            );
            Check(completed.Status == TeamRunStatus.Completed, "Focused run completes: " + completed.Error);
            Check(
                completed.Task?.Specialist == TeamRole.Positions && completed.Steps.Count == 2,
                "Held position routes to position specialist plus reviewer only."
            );
            Check(
                worker.Calls.Select(call => call.Role).SequenceEqual(["positions", "reviewer"]),
                "No full-team invocation for a position review."
            );
            Check(
                completed.Steps.All(step =>
                    step.StartedAt is not null
                    && step.CompletedAt >= step.StartedAt
                    && step.InputDigest?.Length == 64
                    && step.Usage?.InputTokens == 20
                ),
                "Actual usage and exact input digest are retained per attempt."
            );
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    private static async Task DeferredRestart()
    {
        var time = new ManualTimeProvider();
        var store = new FakeCompanyScheduleStore(time.GetUtcNow());
        await store.Save(
            store.Schedule with
            {
                NextRunAt = time.GetUtcNow().AddMinutes(20),
                DeferredReason = "ordinary_research_limit",
                NextRole = TeamRole.News,
            },
            CancellationToken.None
        );
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        using var loop = new CompanyLoop(store, runs, coordinator, new LoopSnapshotReader(), time);
        var state = await loop.Read(CancellationToken.None);
        Check(
            state.Status == CompanyLoopStatus.DeferredBudget
                && state.BudgetReason == "ordinary_research_limit"
                && state.NextRole == TeamRole.News
                && state.NextRunAt == store.Schedule.NextRunAt,
            "Restart preserves budget reason, exact next time and queued specialist."
        );
    }

    private static async Task EvidenceWaitPreservesHistoricalFailure()
    {
        var time = new ManualTimeProvider();
        var store = new FakeCompanyScheduleStore(time.GetUtcNow());
        var runs = new FakeTeamRunStore();
        var failed = new TeamRun(
            Guid.NewGuid(),
            "fixture",
            TeamRunStatus.Failed,
            time.GetUtcNow().AddHours(-1),
            time.GetUtcNow().AddMinutes(-59),
            [],
            "Codex worker failed [worker_runtime_failed]."
        );
        await runs.Save(failed, CancellationToken.None);
        await store.Save(
            store.Schedule with
            {
                NextRunAt = time.GetUtcNow().AddSeconds(1),
                DeferredReason = "waiting_for_evidence",
                LastRunId = failed.Id,
            },
            CancellationToken.None
        );
        using var loop = new CompanyLoop(store, runs, new LoopCoordinator(runs, time), new LoopSnapshotReader(), time);
        Check(
            (await loop.Read(CancellationToken.None)).Status == CompanyLoopStatus.Waiting,
            "Evidence deferral is visible separately from the historical failed report."
        );
        time.Advance(TimeSpan.FromSeconds(2));
        var due = await loop.Read(CancellationToken.None);
        Check(
            due.Status == CompanyLoopStatus.Waiting
                && due.Error is null
                && (await runs.Latest(CancellationToken.None))?.Error == failed.Error,
            "Crossing the recheck deadline must not resurrect a historical error or erase report evidence."
        );
    }

    private static async Task LegacyCooldownRestart()
    {
        var time = new ManualTimeProvider();
        var store = new FakeCompanyScheduleStore(time.GetUtcNow());
        var runs = new FakeTeamRunStore();
        var completed = new TeamRun(
            Guid.NewGuid(),
            "legacy",
            TeamRunStatus.Completed,
            time.GetUtcNow().AddMinutes(-5),
            time.GetUtcNow().AddMinutes(-5),
            [],
            null
        );
        await runs.Save(completed, CancellationToken.None);
        await store.Save(
            store.Schedule with
            {
                LastRunId = completed.Id,
                LastAnalysisAt = time.GetUtcNow().AddMinutes(-5),
                NextRunAt = time.GetUtcNow(),
            },
            CancellationToken.None
        );
        using (var loop = new CompanyLoop(store, runs, new LoopCoordinator(runs, time), new LoopSnapshotReader(), time))
        {
            await loop.Tick(CancellationToken.None);
        }

        var expected = time.GetUtcNow().AddMinutes(10);
        Check(store.Schedule.NextRunAt == expected, "Legacy cooldown persists the exact next run time.");
        time.Advance(TimeSpan.FromMinutes(5));
        var resumedCoordinator = new LoopCoordinator(runs, time);
        using (var reloaded = new CompanyLoop(store, runs, resumedCoordinator, new LoopSnapshotReader(), time))
        {
            await reloaded.Tick(CancellationToken.None);
            var state = await reloaded.Read(CancellationToken.None);
            Check(
                state.Status == CompanyLoopStatus.Waiting
                    && state.NextRunAt == expected
                    && resumedCoordinator.Objectives.Count == 0,
                "Reload keeps the cooldown and does not start early."
            );
            time.Advance(TimeSpan.FromMinutes(5));
            await reloaded.Tick(CancellationToken.None);
            Check(resumedCoordinator.Objectives.Count == 1, "Research starts at the preserved cooldown boundary.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
