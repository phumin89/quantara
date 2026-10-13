using System.Net;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class SignalLoopChecks
{
    public static async Task Run()
    {
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
            coordinator.Objectives[0].Contains("500 THB realized net profit")
                && coordinator.Objectives[0].Length <= 2000,
            "daily net target with bounded objective"
        );
        Check((await loop.Read(CancellationToken.None)).DailyNetProfitTargetThb == 500m, "visible daily target");
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
        await runs.Save(second with { Status = TeamRunStatus.Completed }, CancellationToken.None);
        signal.Notice = Notice("signal-b", time.GetUtcNow().AddMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));
        await loop.Tick(CancellationToken.None);
        Check(coordinator.Objectives.Count == 2, "same signal does not retrigger");

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
