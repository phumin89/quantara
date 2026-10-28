using System.Net;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class SignalLoopChecks
{
    public static async Task Run()
    {
        await DeadlineAlignment();
        await ReviewSchedulingChecks();
        await SignalExpiryRecoveryChecks();
        var time = new ManualTimeProvider();
        var schedule = new FakeCompanyScheduleStore(time.GetUtcNow());
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        var signal = new FakeMarketSignalReader { Notice = Notice("signal-a", time.GetUtcNow()) };
        var portfolio = new LoopSnapshotReader();
        using var loop = new CompanyLoop(schedule, runs, coordinator, portfolio, time, signal);

        await loop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 1, "normal hourly round");
        Check(
            coordinator.Objectives[0].Contains("supplied account-checked portfolio evidence", StringComparison.Ordinal)
                && coordinator.Objectives[0].Contains("Target unavailable", StringComparison.Ordinal)
                && !coordinator.Objectives[0].Contains("Current portfolio goal: reach THB", StringComparison.Ordinal)
                && coordinator.Objectives[0].Length <= 2000,
            "research uses the supplied account goal without a numeric target or deadline fallback"
        );
        var overview = await loop.Read(CancellationToken.None);
        Check(overview.CurrentGoal is null, "overview without current account evidence does not invent a goal");
        Check(overview.DailyNetProfitTargetThb == 500m, "visible legacy daily target");
        var first =
            await runs.Latest(CancellationToken.None) ?? throw new InvalidOperationException("first run missing");
        await runs.Save(first with { Status = TeamRunStatus.Completed }, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(15));
        signal.Notice = Notice("signal-b", time.GetUtcNow());
        await loop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 2, "fresh signal starts early round");
        Check(schedule.Schedule.NextRunAt == time.GetUtcNow().AddMinutes(45), "early round preserves hourly schedule");
        Check(
            schedule.Schedule.LastSignalKey == signal.Notice.Key
                && schedule.Schedule.LastAnalysisAt == time.GetUtcNow(),
            "signal key/time persisted"
        );

        var second =
            await runs.Latest(CancellationToken.None) ?? throw new InvalidOperationException("second run missing");
        await runs.Save(
            second with
            {
                Status = TeamRunStatus.Completed,
                Learning = new LearningSnapshot(time.GetUtcNow(), [], time.GetUtcNow().AddHours(1)),
            },
            CancellationToken.None
        );
        signal.Notice = Notice("signal-b", time.GetUtcNow().AddMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));
        await loop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 2, "same signal does not retrigger");
        Check(
            schedule.Schedule.NextRunAt == second.CreatedAt.AddMinutes(45),
            "signal learning deadline preserves hourly cadence"
        );

        using var restarted = new CompanyLoop(schedule, runs, coordinator, portfolio, time, signal);
        await restarted.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 2, "same signal survives restart");

        signal.Notice = Notice("signal-c", time.GetUtcNow());
        await restarted.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 2, "changed signal respects fifteen minute cooldown");

        var active =
            await runs.Latest(CancellationToken.None) ?? throw new InvalidOperationException("active run missing");
        await runs.Save(active with { Status = TeamRunStatus.Running }, CancellationToken.None);
        signal.Notice = Notice("signal-d", time.GetUtcNow().AddMinutes(16));
        time.Advance(TimeSpan.FromMinutes(16));
        await restarted.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 2, "active run blocks overlap");
        await runs.Save(active with { Status = TeamRunStatus.Completed }, CancellationToken.None);

        await restarted.SetEnabled(false, CancellationToken.None);
        signal.Notice = Notice("signal-e", time.GetUtcNow());
        await restarted.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 2, "disabled loop does not run");

        await restarted.SetEnabled(true, CancellationToken.None);
        signal.Notice = Notice("signal-f", time.GetUtcNow().AddMinutes(-3));
        time.Advance(TimeSpan.FromMinutes(1));
        await restarted.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 3, "hourly schedule still runs when signal is stale");
        var third =
            await runs.Latest(CancellationToken.None) ?? throw new InvalidOperationException("third run missing");
        await runs.Save(third with { Status = TeamRunStatus.Completed }, CancellationToken.None);

        var failedSignal = new FakeMarketSignalReader { Failure = new HttpRequestException("signal fixture failure") };
        using var failureLoop = new CompanyLoop(schedule, runs, coordinator, portfolio, time, failedSignal);
        time.Advance(TimeSpan.FromHours(1));
        await failureLoop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 4, "signal HTTP failure does not block hourly work");

        await ReadSignalReaderChecks(time);
        Console.WriteLine("Signal loop checks passed.");
    }

    private static async Task SignalExpiryRecoveryChecks()
    {
        var time = new ManualTimeProvider();
        var schedule = new FakeCompanyScheduleStore(time.GetUtcNow());
        await schedule.Save(
            schedule.Schedule with
            {
                NextRunAt = time.GetUtcNow().AddMinutes(30),
            },
            CancellationToken.None
        );
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        var portfolio = new LoopSnapshotReader { Available = false };
        var signal = new FakeMarketSignalReader { Notice = Notice("expiring", time.GetUtcNow()) };
        using var loop = new CompanyLoop(schedule, runs, coordinator, portfolio, time, signal);

        await loop.Tick(CancellationToken.None);
        var waiting = await loop.Read(CancellationToken.None);
        Check(
            waiting.Status == CompanyLoopStatus.WaitingForPortfolio && waiting.Error is not null,
            "initial signal wait records portfolio error"
        );
        var deadline = schedule.Schedule.NextRunAt;
        signal.Notice = null;
        portfolio.Available = true;
        time.Advance(TimeSpan.FromMinutes(1));
        await loop.Tick(CancellationToken.None);
        var recovered = await loop.Read(CancellationToken.None);
        Check(
            recovered.Status == CompanyLoopStatus.Waiting && recovered.Error is null,
            "expired signal clears transient portfolio error"
        );
        Check(
            schedule.Schedule.NextRunAt == deadline && schedule.Schedule.LastSignalKey is null,
            "recovery preserves deadline and leaves signal unconsumed"
        );
        Check(coordinator.Objectives.Count == 0, "expired signal does not start work");
        signal.Notice = Notice("fresh", time.GetUtcNow());
        time.Advance(TimeSpan.FromMinutes(1));
        await loop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 1, "fresh signal starts after recovery");
        await loop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 1, "fresh signal starts exactly once");

        var failureTime = new ManualTimeProvider();
        var failureSchedule = new FakeCompanyScheduleStore(failureTime.GetUtcNow());
        var failureRuns = new FakeTeamRunStore();
        var failingCoordinator = new LoopCoordinator(failureRuns, failureTime) { FailStart = true };
        var failureSignal = new FakeMarketSignalReader { Notice = Notice("uncertain", failureTime.GetUtcNow()) };
        using var failureLoop = new CompanyLoop(
            failureSchedule,
            failureRuns,
            failingCoordinator,
            new LoopSnapshotReader(),
            failureTime,
            failureSignal
        );
        await failureLoop.Tick(CancellationToken.None);
        failureSignal.Notice = null;
        failureTime.Advance(TimeSpan.FromMinutes(1));
        await failureLoop.Tick(CancellationToken.None);
        var failure = await failureLoop.Read(CancellationToken.None);
        Check(
            failure.Status == CompanyLoopStatus.Error && failure.Error is not null,
            "uncertain start failure remains visible without work"
        );
    }

    private static async Task ReviewSchedulingChecks()
    {
        foreach (
            var scenario in new[]
            {
                (Name: "future", Age: 30, Review: 5, Expiry: 120, Foreign: false, WrongRun: false, Expected: 5),
                (Name: "overdue", Age: 30, Review: -5, Expiry: 120, Foreign: false, WrongRun: false, Expected: -5),
                (Name: "cooldown", Age: 5, Review: 2, Expiry: 120, Foreign: false, WrongRun: false, Expected: 10),
                (Name: "expired", Age: 30, Review: -5, Expiry: -1, Foreign: false, WrongRun: false, Expected: 60),
                (Name: "foreign", Age: 30, Review: 5, Expiry: 120, Foreign: true, WrongRun: false, Expected: 60),
                (Name: "wrong run", Age: 30, Review: 5, Expiry: 120, Foreign: false, WrongRun: true, Expected: 60),
                (
                    Name: "hourly fallback",
                    Age: 30,
                    Review: 90,
                    Expiry: 120,
                    Foreign: false,
                    WrongRun: false,
                    Expected: 60
                ),
            }
        )
        {
            var time = new ManualTimeProvider();
            var now = time.GetUtcNow();
            var schedule = new FakeCompanyScheduleStore(now);
            var runs = new FakeTeamRunStore();
            var coordinator = new LoopCoordinator(runs, time);
            var account = new string('a', 64);
            var created = now.AddMinutes(-scenario.Age);
            var id = Guid.NewGuid();
            var assessment = new ResearchAssessment(
                "research-assessment-v1",
                scenario.Foreign ? new string('b', 64) : account,
                scenario.WrongRun ? Guid.NewGuid() : id,
                null,
                created,
                now.AddMinutes(scenario.Expiry),
                [],
                [
                    new PositionAssessment(
                        "BTC",
                        "Keep",
                        "Observed entry",
                        "Monitor exits",
                        now.AddMinutes(scenario.Review),
                        now.AddMinutes(scenario.Expiry),
                        []
                    ),
                ],
                [new ResearchOpportunity("ETH", "Review setup", "Fresh book", now.AddMinutes(scenario.Review + 1))]
            );
            var run = new TeamRun(
                id,
                "review",
                TeamRunStatus.Completed,
                created,
                created,
                [],
                null,
                account,
                Assessment: assessment
            );
            await runs.Save(run, CancellationToken.None);
            await schedule.Save(
                schedule.Schedule with
                {
                    LastRunId = id,
                    NextRunAt = now.AddHours(1),
                    LastAnalysisAt = created,
                },
                CancellationToken.None
            );
            using var loop = new CompanyLoop(schedule, runs, coordinator, new LoopSnapshotReader(), time);
            await loop.Tick(CancellationToken.None);
            if (scenario.Expected < 0)
            {
                Check(coordinator.Objectives.Count == 1, "overdue valid review starts on next tick");
                await loop.Tick(CancellationToken.None);
                Check(coordinator.Objectives.Count == 1, "active review never overlaps");
                continue;
            }

            Check(schedule.Schedule.NextRunAt == now.AddMinutes(scenario.Expected), scenario.Name + " deadline");
            Check(coordinator.Objectives.Count == 0, scenario.Name + " does not start early");
            using var restarted = new CompanyLoop(schedule, runs, coordinator, new LoopSnapshotReader(), time);
            if (scenario.Expected < 60)
            {
                coordinator.FailStart = scenario.Name == "future";
                time.Advance(TimeSpan.FromMinutes(scenario.Expected));
                await restarted.Tick(CancellationToken.None);
                Check(coordinator.Objectives.Count == 1, scenario.Name + " due review survives restart");
                if (coordinator.FailStart)
                {
                    using var afterFailure = new CompanyLoop(
                        schedule,
                        runs,
                        coordinator,
                        new LoopSnapshotReader(),
                        time
                    );
                    var deferred = schedule.Schedule.NextRunAt;
                    time.Advance(TimeSpan.FromMinutes(16));
                    await afterFailure.Tick(CancellationToken.None);
                    Check(
                        coordinator.Objectives.Count == 1 && schedule.Schedule.NextRunAt == deferred,
                        "restart does not reapply an old deadline after a failed start"
                    );
                }
            }
        }
    }

    private static async Task DeadlineAlignment()
    {
        var time = new ManualTimeProvider();
        var schedule = new FakeCompanyScheduleStore(time.GetUtcNow());
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        using var loop = new CompanyLoop(schedule, runs, coordinator, new LoopSnapshotReader(), time);
        await loop.Tick(CancellationToken.None);
        var first = await runs.Latest(CancellationToken.None) ?? throw new InvalidOperationException("run missing");
        var observedAt = time.GetUtcNow().AddSeconds(20);
        var deadline = observedAt.AddHours(1);
        time.Advance(TimeSpan.FromSeconds(30));
        await runs.Save(
            first with
            {
                Status = TeamRunStatus.Completed,
                Learning = new LearningSnapshot(observedAt, [], deadline),
            },
            CancellationToken.None
        );
        await loop.Tick(CancellationToken.None);
        Check(schedule.Schedule.NextRunAt == deadline, "hourly schedule waits for captured learning deadline");
        time.Advance(TimeSpan.FromSeconds(3570));
        await loop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 1, "no premature endpoint sample");
        time.Advance(TimeSpan.FromSeconds(20));
        await loop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 2, "hourly report starts at mature endpoint");
    }

    private static async Task ReadSignalReaderChecks(ManualTimeProvider time)
    {
        var handler = new SignalHttpHandler(request =>
        {
            Check(request.Method == HttpMethod.Get, "signal reader only uses GET");
            return SignalHttpHandler.Json(
                new
                {
                    available = true,
                    connected = true,
                    checkedAt = time.GetUtcNow(),
                    signals = Enumerable
                        .Range(1, 16)
                        .Select(index => new
                        {
                            id = "signal-" + index,
                            symbol = "C" + index + "_THB",
                            direction = "breakout",
                            candleAt = time.GetUtcNow().ToUnixTimeSeconds() - 300,
                            detectedAt = time.GetUtcNow(),
                            price = 2_000_000m,
                        })
                        .ToArray(),
                }
            );
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://signals.test/") };
        var reader = new MarketSignalReader(client, time);
        var first = await reader.Read(CancellationToken.None) ?? throw new InvalidOperationException("signal missing");
        var second = await reader.Read(CancellationToken.None) ?? throw new InvalidOperationException("signal missing");
        Check(first.Key == second.Key && handler.Requests == 2, "stable reader key");
        Check(
            first.Description.Length < 400 && first.Description.Contains("16 total"),
            "all sixteen signals with bounded summary"
        );

        var staleHandler = new SignalHttpHandler(_ =>
            SignalHttpHandler.Json(
                new
                {
                    available = true,
                    connected = true,
                    checkedAt = time.GetUtcNow().AddMinutes(-3),
                    signals = Array.Empty<object>(),
                }
            )
        );
        using var staleClient = new HttpClient(staleHandler) { BaseAddress = new Uri("https://signals.test/") };
        Check(
            await new MarketSignalReader(staleClient, time).Read(CancellationToken.None) is null,
            "stale signal ignored"
        );

        var unavailableHandler = new SignalHttpHandler(_ =>
            SignalHttpHandler.Json(
                new
                {
                    available = false,
                    connected = false,
                    checkedAt = time.GetUtcNow(),
                    signals = Array.Empty<object>(),
                }
            )
        );
        using var unavailableClient = new HttpClient(unavailableHandler)
        {
            BaseAddress = new Uri("https://signals.test/"),
        };
        Check(
            await new MarketSignalReader(unavailableClient, time).Read(CancellationToken.None) is null,
            "unavailable signal ignored"
        );

        var failedHandler = new SignalHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var failedClient = new HttpClient(failedHandler) { BaseAddress = new Uri("https://signals.test/") };
        try
        {
            await new MarketSignalReader(failedClient, time).Read(CancellationToken.None);
            throw new InvalidOperationException("failed signal response accepted");
        }
        catch (HttpRequestException) { }
    }

    private static MarketSignalNotice Notice(string key, DateTimeOffset checkedAt)
    {
        return new MarketSignalNotice(key, "BTC_THB breakout", checkedAt);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Signal loop check failed: {name}");
        }
    }
}
