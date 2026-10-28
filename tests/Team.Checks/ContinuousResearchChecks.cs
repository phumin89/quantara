using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ContinuousResearchChecks
{
    private static readonly string[] newsEventIds = ["A", "B"];

    public static async Task Run()
    {
        Routing();
        NewsRevisionRouting();
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

    private static void NewsRevisionRouting()
    {
        var now = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        var account = new string('a', 64);
        using var evidence = JsonDocument.Parse($$"""{"accountKey":"{{account}}","positions":[]}""");
        const string original = """{"events":[{"eventId":"A","revision":1},{"eventId":"B","revision":1}]}""";
        const string retained = """{"events":[{"eventId":"B","revision":1}]}""";
        const string revised = """{"events":[{"eventId":"B","revision":2}]}""";
        var task = TeamResearchRouter
            .Candidates(evidence.RootElement, original, [], now, false)
            .Single(candidate => candidate.Specialist == TeamRole.News);
        var previous = new TeamRun(
            Guid.NewGuid(),
            "fixture",
            TeamRunStatus.Completed,
            now,
            now,
            [],
            null,
            account,
            Task: task
        );
        Check(
            task.NewsRevisions is not null && task.NewsRevisions.SequenceEqual(["A@1", "B@1"]),
            "A news task retains distinct event/revision identities."
        );
        Check(
            TeamResearchRouter
                .Candidates(evidence.RootElement, original, [], now, false)
                .Where(candidate => candidate.Specialist != TeamRole.News)
                .All(candidate => candidate.NewsRevisions is null),
            "Other specialties do not duplicate news revision metadata."
        );
        Check(
            !TeamResearchRouter
                .Candidates(evidence.RootElement, retained, [previous], now.AddMinutes(16), false)
                .Any(candidate => candidate.Specialist == TeamRole.News),
            "Expiry of another news event must not reschedule an unchanged reviewed revision."
        );
        Check(
            TeamResearchRouter
                .Candidates(evidence.RootElement, revised, [previous], now.AddMinutes(16), false)
                .Any(candidate => candidate.Specialist == TeamRole.News),
            "A genuinely new news revision remains eligible after cooldown."
        );
        Check(
            TeamResearchRouter
                .Candidates(
                    evidence.RootElement,
                    retained,
                    [previous with { AccountKey = new string('b', 64) }],
                    now.AddMinutes(16),
                    false
                )
                .Any(candidate => candidate.Specialist == TeamRole.News),
            "Another account's review does not consume a news revision."
        );
        Check(
            !HasNews(revised, [previous], now.AddMinutes(14)),
            "A new revision does not bypass the existing news cooldown."
        );
        Check(
            !HasNews(retained, [previous with { Status = TeamRunStatus.Failed }], now.AddMinutes(16)),
            "A failed attempt retains its news claim instead of replaying unchanged input."
        );
        Check(
            HasNews(retained, [previous with { CreatedAt = now.AddMinutes(17) }], now.AddMinutes(16)),
            "Future records cannot claim a revision in current history."
        );
        const string reordered =
            """{"events":[{"eventId":"B","revision":1},{"eventId":"A","revision":1},{"eventId":"B","revision":1}]}""";
        var reorderedTask = TeamResearchRouter
            .Candidates(evidence.RootElement, reordered, [], now, false)
            .Single(candidate => candidate.Specialist == TeamRole.News);
        Check(
            reorderedTask.NewsDigest == task.NewsDigest
                && task.NewsDigest == TeamResearchRouter.Digest("A@1|B@1")
                && !HasNews(reordered, [previous], now.AddMinutes(16)),
            "Reordering/duplicate input preserves the legacy digest and does not create work."
        );
        var separateReviews = ContinuousResearchChecks
            .newsEventIds.Select(eventId =>
                previous with
                {
                    Task = TeamResearchRouter
                        .Candidates(
                            evidence.RootElement,
                            $$"""{"events":[{"eventId":"{{eventId}}","revision":1}]}""",
                            [],
                            now,
                            false
                        )
                        .Single(candidate => candidate.Specialist == TeamRole.News),
                }
            )
            .ToArray();
        Check(
            !HasNews(original, separateReviews, now.AddMinutes(16)),
            "A packet containing revisions already claimed in separate runs creates no duplicate work."
        );
        var restored =
            JsonSerializer.Deserialize<TeamRun>(JsonSerializer.Serialize(previous))
            ?? throw new InvalidOperationException("Research round trip returned null.");
        Check(
            restored.Task?.NewsRevisions is not null
                && restored.Task.NewsRevisions.SequenceEqual(task.NewsRevisions ?? [])
                && !HasNews(retained, [restored], now.AddMinutes(16)),
            "Persisted news revisions prevent subset replays after a restart."
        );
        var legacyJson = JsonSerializer.Serialize(
            new
            {
                task.Specialist,
                task.InputDigest,
                task.PolicyHash,
                task.EvidenceAt,
                task.ExpiresAt,
                task.NewsDigest,
                task.StudyDigest,
            }
        );
        var legacy =
            JsonSerializer.Deserialize<ResearchTask>(legacyJson)
            ?? throw new InvalidOperationException("Legacy research task returned null.");
        Check(
            legacy.NewsRevisions is null
                && !HasNews(original, [previous with { Task = legacy }], now.AddMinutes(16))
                && HasNews(retained, [previous with { Task = legacy }], now.AddMinutes(16)),
            "Legacy records retain exact-digest deduplication without inventing missing per-event history."
        );

        bool HasNews(string packet, IReadOnlyList<TeamRun> history, DateTimeOffset at) =>
            TeamResearchRouter
                .Candidates(evidence.RootElement, packet, history, at, false)
                .Any(candidate => candidate.Specialist == TeamRole.News);
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
        var portfolio = new LoopSnapshotReader();
        using var loop = new CompanyLoop(store, runs, new LoopCoordinator(runs, time), portfolio, time);
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
        portfolio.Available = false;
        await loop.Tick(CancellationToken.None);
        var unavailable = await loop.Read(CancellationToken.None);
        Check(
            unavailable.Status == CompanyLoopStatus.WaitingForPortfolio
                && unavailable.Error == "Waiting for verified portfolio data before the next analysis."
                && (await runs.Latest(CancellationToken.None))?.Error == failed.Error,
            "A current portfolio failure takes precedence over a retained evidence deferral."
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
