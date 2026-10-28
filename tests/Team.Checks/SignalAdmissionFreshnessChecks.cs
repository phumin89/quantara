using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class SignalAdmissionFreshnessChecks
{
    public static async Task Run()
    {
        foreach (var hourly in new[] { false, true })
        {
            await ExpiredDuringReadiness(hourly);
            await ExpiredDuringSignalRead(hourly);
        }

        await FreshSignalIsConsumedOnce();
        await PostReadClockAdmitsAllowedFutureTimestamp();
    }

    private static async Task ExpiredDuringReadiness(bool hourly)
    {
        var time = new ManualTimeProvider();
        var startedAt = time.GetUtcNow();
        var schedule = await Schedule(startedAt, hourly);
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        var portfolio = new AdvancingLoopSnapshotReader(time, TimeSpan.FromSeconds(2));
        var signal = new FakeMarketSignalReader
        {
            Notice = new MarketSignalNotice("expires-in-readiness", "BTC_THB breakout", startedAt.AddSeconds(-119)),
        };
        using var loop = new CompanyLoop(schedule, runs, coordinator, portfolio, time, signal);

        await loop.Tick(CancellationToken.None);

        Check(portfolio.Reads == 1 && signal.Reads == 1, "readiness expiry uses one signal and readiness probe");
        Check(time.GetUtcNow() == startedAt.AddSeconds(2), "readiness advances only the fake clock");
        await CheckExpiredAdmission(schedule, runs, coordinator, loop, time, startedAt, hourly);
    }

    private static async Task ExpiredDuringSignalRead(bool hourly)
    {
        var time = new ManualTimeProvider();
        var startedAt = time.GetUtcNow();
        var schedule = await Schedule(startedAt, hourly);
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        var portfolio = new AdvancingLoopSnapshotReader(time, TimeSpan.Zero);
        var signal = new AdvancingMarketSignalReader(
            time,
            TimeSpan.FromSeconds(2),
            () => new MarketSignalNotice("expires-in-signal-read", "BTC_THB breakout", startedAt.AddSeconds(-119))
        );
        using var loop = new CompanyLoop(schedule, runs, coordinator, portfolio, time, signal);

        await loop.Tick(CancellationToken.None);

        Check(signal.Reads == 1, "signal expiry uses one signal read");
        Check(portfolio.Reads == (hourly ? 1 : 0), "expired signal alone does not probe portfolio readiness");
        Check(time.GetUtcNow() == startedAt.AddSeconds(2), "signal read advances only the fake clock");
        await CheckExpiredAdmission(schedule, runs, coordinator, loop, time, startedAt, hourly);
    }

    private static async Task FreshSignalIsConsumedOnce()
    {
        var time = new ManualTimeProvider();
        var startedAt = time.GetUtcNow();
        var schedule = await Schedule(startedAt, false);
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        var portfolio = new AdvancingLoopSnapshotReader(time, TimeSpan.FromMilliseconds(500));
        var signal = new FakeMarketSignalReader
        {
            Notice = new MarketSignalNotice("fresh-signal", "BTC_THB breakout", startedAt.AddSeconds(-119)),
        };
        using var loop = new CompanyLoop(schedule, runs, coordinator, portfolio, time, signal);

        await loop.Tick(CancellationToken.None);

        Check(coordinator.Objectives.Count == 1, "fresh signal starts one research round");
        Check(HasMarketMarker(coordinator.Objectives[0]), "fresh signal retains market trigger marker");
        Check(schedule.Schedule.LastSignalKey == "fresh-signal", "fresh signal key is consumed");
        Check(
            schedule.Schedule.LastAnalysisAt == time.GetUtcNow(),
            "fresh signal claim uses readiness completion time"
        );
        Check(schedule.Schedule.NextRunAt == startedAt.AddMinutes(30), "signal research preserves hourly deadline");
        var first =
            await runs.Latest(CancellationToken.None)
            ?? throw new InvalidOperationException("Fresh-signal run is missing.");
        await runs.Save(first with { Status = TeamRunStatus.Completed }, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(16));
        signal.Notice = new MarketSignalNotice("fresh-signal", "BTC_THB breakout", time.GetUtcNow());

        await loop.Tick(CancellationToken.None);

        Check(signal.Reads == 2, "same signal is reread after cooldown");
        Check(coordinator.Objectives.Count == 1 && portfolio.Reads == 1, "consumed key never triggers another round");
        Check(
            schedule.Schedule.LastAnalysisAt == first.CreatedAt,
            "duplicate key does not consume another analysis time"
        );
    }

    private static async Task PostReadClockAdmitsAllowedFutureTimestamp()
    {
        var time = new ManualTimeProvider();
        var startedAt = time.GetUtcNow();
        var schedule = await Schedule(startedAt, false);
        var runs = new FakeTeamRunStore();
        var coordinator = new LoopCoordinator(runs, time);
        var portfolio = new AdvancingLoopSnapshotReader(time, TimeSpan.Zero);
        var signal = new AdvancingMarketSignalReader(
            time,
            TimeSpan.FromSeconds(2),
            () => new MarketSignalNotice("allowed-future", "BTC_THB breakout", time.GetUtcNow().AddSeconds(4))
        );
        using var loop = new CompanyLoop(schedule, runs, coordinator, portfolio, time, signal);

        await loop.Tick(CancellationToken.None);

        Check(time.GetUtcNow() == startedAt.AddSeconds(2), "signal collection advances the fake clock");
        Check(signal.Reads == 1 && portfolio.Reads == 1, "admitted signal probes readiness once");
        Check(coordinator.Objectives.Count == 1, "post-read clock accepts timestamp within five-second allowance");
        Check(HasMarketMarker(coordinator.Objectives[0]), "allowed future signal is market triggered");
        Check(schedule.Schedule.LastSignalKey == "allowed-future", "allowed future signal key is consumed");
        Check(schedule.Schedule.LastAnalysisAt == time.GetUtcNow(), "post-read claim uses the current fake time");
    }

    private static async Task<FakeCompanyScheduleStore> Schedule(DateTimeOffset now, bool hourly)
    {
        var store = new FakeCompanyScheduleStore(now);
        await store.Save(
            store.Schedule with
            {
                NextRunAt = hourly ? now : now.AddMinutes(30),
                LastSignalKey = "previous-signal",
                LastAnalysisAt = now.AddMinutes(-31),
            },
            CancellationToken.None
        );

        return store;
    }

    private static async Task CheckExpiredAdmission(
        FakeCompanyScheduleStore schedule,
        FakeTeamRunStore runs,
        LoopCoordinator coordinator,
        CompanyLoop loop,
        ManualTimeProvider time,
        DateTimeOffset startedAt,
        bool hourly
    )
    {
        Check(schedule.Schedule.LastSignalKey == "previous-signal", "expired signal key remains unconsumed");
        if (hourly)
        {
            Check(coordinator.Objectives.Count == 1, "expired signal preserves one due hourly round");
            Check(!HasMarketMarker(coordinator.Objectives[0]), "hourly fallback has no market trigger marker");
            Check(schedule.Schedule.LastAnalysisAt == time.GetUtcNow(), "hourly claim uses admission time");
            Check(schedule.Schedule.NextRunAt == time.GetUtcNow().AddHours(1), "hourly next run uses admission time");
        }
        else
        {
            Check(coordinator.Objectives.Count == 0, "expired signal does not start research early");
            Check(await runs.Latest(CancellationToken.None) is null, "expired signal creates no run");
            Check(
                schedule.Schedule.LastAnalysisAt == startedAt.AddMinutes(-31),
                "expired signal consumes no analysis time"
            );
            Check(schedule.Schedule.NextRunAt == startedAt.AddMinutes(30), "expired signal preserves future deadline");
            Check(
                (await loop.Read(CancellationToken.None)).Status == CompanyLoopStatus.Waiting,
                "expired signal leaves loop waiting"
            );
        }
    }

    private static bool HasMarketMarker(string objective) =>
        objective.Contains("Market alert (unvalidated research heuristic, not an order)", StringComparison.Ordinal);

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Signal admission freshness check failed: {name}");
        }
    }
}
