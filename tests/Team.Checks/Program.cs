using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;
using Team.Checks;

await Success();
await ResearchMemory();
await BusyAndUnrelatedStop();
await Cancellation();
await Failure();
await MissingConfiguration();
await LateOutput();
await SnapshotWhitelist();
await ProcessBoundary();
InvalidObjective();
await DailyBriefBoundary();
await CompanyLoopChecks.Run();
Console.WriteLine("Team checks passed.");

static async Task Success()
{
    var store = new FakeTeamRunStore();
    var worker = new FakeCodexWorker((role, _, _) => Task.FromResult($"{role}-output"));
    await using var coordinator = await Start(store, worker);
    var run = await coordinator.Start("objective", CancellationToken.None);
    var result = await store.WaitFor(run.Id, item => item.Status == TeamRunStatus.Completed, TimeoutToken());
    Check(
        worker.Calls.Count == 3
            && worker.Calls[1].Previous[0].Output == "ceo-output"
            && worker.Calls[2].Previous[1].Output == "analyst-output",
        "ordered handoff"
    );
    Check(result.Steps.All(step => step.Status == TeamRunStatus.Completed), "success status");
    Check(worker.Calls.All(call => call.Portfolio is not null), "portfolio snapshot");
}

static async Task ResearchMemory()
{
    var store = new FakeTeamRunStore();
    var priorId = Guid.NewGuid();
    var review = new string('ก', 4000) + "TASK-001 Pending: inspect retained fills";
    await store.Save(
        new TeamRun(
            priorId,
            "old",
            TeamRunStatus.Completed,
            DateTimeOffset.UtcNow.AddDays(-3),
            DateTimeOffset.UtcNow.AddDays(-3),
            [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, review, null)],
            null
        ),
        CancellationToken.None
    );
    var worker = new FakeCodexWorker((role, _, _) => Task.FromResult(role));
    await using var coordinator = await Start(store, worker);
    var run = await coordinator.Start($"Research. Previous review run ID: {priorId:D}", CancellationToken.None);
    await store.WaitFor(run.Id, item => item.Status == TeamRunStatus.Completed, TimeoutToken());
    foreach (var call in worker.Calls)
    {
        using var context = JsonDocument.Parse(call.Portfolio ?? throw new InvalidOperationException("No context"));
        Check(
            context.RootElement.GetProperty("priorReview").GetString() == review,
            "full durable review beyond 1000 characters and across dates"
        );
        Check(
            context.RootElement.GetProperty("evidence").GetProperty("positions").GetArrayLength() == 1,
            "current evidence retained"
        );
    }
}

static async Task BusyAndUnrelatedStop()
{
    var store = new FakeTeamRunStore();
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var worker = new FakeCodexWorker(
        async (role, _, cancellationToken) =>
        {
            if (role == "ceo")
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return "done";
        }
    );
    await using var coordinator = await Start(store, worker);
    var run = await coordinator.Start("first", CancellationToken.None);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    try
    {
        await coordinator.Start("second", CancellationToken.None);
        throw new InvalidOperationException("busy accepted");
    }
    catch (TeamBusyException) { }

    Check(await coordinator.RequestStop(Guid.NewGuid(), CancellationToken.None) is null, "unrelated stop");
    release.SetResult();
    await store.WaitFor(run.Id, item => item.Status == TeamRunStatus.Completed, TimeoutToken());
}

static async Task Cancellation()
{
    var store = new FakeTeamRunStore();
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var worker = new FakeCodexWorker(
        async (_, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "never";
        }
    );
    await using var coordinator = await Start(store, worker);
    var run = await coordinator.Start("cancel", CancellationToken.None);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Check(
        (await coordinator.RequestStop(run.Id, CancellationToken.None))?.Status == TeamRunStatus.Cancelled,
        "cancel status"
    );
    var result = await store.WaitFor(run.Id, item => item.Status == TeamRunStatus.Cancelled, TimeoutToken());
    Check(
        worker.Calls.Count == 1 && result.Steps.Skip(1).All(step => step.Status == TeamRunStatus.Cancelled),
        "cancel skips later roles"
    );
}

static async Task Failure()
{
    var store = new FakeTeamRunStore();
    var worker = new FakeCodexWorker(
        (role, _, _) =>
            role == "analyst" ? throw new InvalidOperationException("expected") : Task.FromResult("ceo-output")
    );
    await using var coordinator = await Start(store, worker);
    var run = await coordinator.Start("failure", CancellationToken.None);
    var result = await store.WaitFor(run.Id, item => item.Status == TeamRunStatus.Failed, TimeoutToken());
    Check(result.Steps[0].Output == "ceo-output" && worker.Calls.Count == 2, "failure retains CEO output");
}

static async Task MissingConfiguration()
{
    var store = new FakeTeamRunStore();
    var worker = new FakeCodexWorker((_, _, _) => Task.FromResult("never"), false);
    await using var coordinator = await Start(store, worker);
    try
    {
        await coordinator.Start("unavailable", CancellationToken.None);
        throw new InvalidOperationException("missing config accepted");
    }
    catch (TeamUnavailableException) { }

    Check(await store.Latest(CancellationToken.None) is null, "missing config does not persist");
}

static void InvalidObjective()
{
    var handler = new CreateTeamRunHandler(new FakeCoordinator());
    try
    {
        handler.Handle(new CreateTeamRunCommand(" "), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        throw new InvalidOperationException("invalid objective accepted");
    }
    catch (ArgumentException) { }
}

static async Task DailyBriefBoundary()
{
    var store = new FakeTeamRunStore();
    var day = new DateOnly(2026, 10, 13);
    await store.Save(
        RunAt(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new DateTimeOffset(2026, 10, 12, 16, 59, 59, TimeSpan.Zero)
        ),
        CancellationToken.None
    );
    await store.Save(
        RunAt(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            new DateTimeOffset(2026, 10, 12, 17, 0, 0, TimeSpan.Zero)
        ),
        CancellationToken.None
    );
    await store.Save(
        RunAt(
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            new DateTimeOffset(2026, 10, 13, 16, 59, 59, TimeSpan.Zero)
        ),
        CancellationToken.None
    );
    await store.Save(
        RunAt(
            Guid.Parse("00000000-0000-0000-0000-000000000004"),
            new DateTimeOffset(2026, 10, 13, 17, 0, 0, TimeSpan.Zero)
        ),
        CancellationToken.None
    );
    var result = await new GetDailyBriefHandler(store).Handle(new GetDailyBriefQuery(day), CancellationToken.None);
    Check(
        result
            .Select(run => run.Id)
            .SequenceEqual(
                new[]
                {
                    Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    Guid.Parse("00000000-0000-0000-0000-000000000003"),
                }
            ),
        "daily brief Bangkok boundaries"
    );
}

static TeamRun RunAt(Guid id, DateTimeOffset createdAt)
{
    return new TeamRun(id, "brief", TeamRunStatus.Completed, createdAt, createdAt, [], null);
}

static async Task<TeamCoordinatorLease> Start(FakeTeamRunStore store, FakeCodexWorker worker)
{
    var coordinator = new TeamCoordinator(store, worker, new FakePortfolioSnapshotReader());
    await coordinator.StartAsync(CancellationToken.None);
    await coordinator.Initialization.WaitAsync(TimeSpan.FromSeconds(5));
    return new TeamCoordinatorLease(coordinator);
}

static async Task LateOutput()
{
    var store = new FakeTeamRunStore();
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var worker = new FakeCodexWorker(
        (_, _, _) =>
        {
            started.TrySetResult();
            return release.Task;
        }
    );
    await using var coordinator = await Start(store, worker);
    var run = await coordinator.Start("late report", CancellationToken.None);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await coordinator.RequestStop(run.Id, CancellationToken.None);
    release.TrySetResult("late response");
    // Lease StopAsync waits for the actual hosted execution to finish.
    await coordinator.DisposeAsync();
    var saved = await store.Find(run.Id, CancellationToken.None);
    Check(
        saved?.Status == TeamRunStatus.Cancelled
            && worker.Calls.Count == 1
            && saved.Steps.All(step => step.Output is null),
        "late output cannot overwrite Stop"
    );
}

static async Task SnapshotWhitelist()
{
    var snapshot = new
    {
        positions = new[]
        {
            new
            {
                symbol = "BTC",
                quantity = 1,
                currentPrice = 100,
                value = 100,
                averageCost = (decimal?)null,
                costStatus = "Unknown",
                apiSecret = "private",
            },
        },
        summary = new
        {
            totalAssets = 100,
            availableThb = 200,
            holdingCount = 1,
            accountKey = "private",
        },
        updatedAt = (string?)"2026-10-12T12:00:00Z",
        priceUpdatedAt = (string?)null,
        historyUpdatedAt = (string?)null,
        historyCoverage = "Limited",
        syncing = false,
        error = (string?)null,
        credentials = "private",
    };
    using var handler = new SnapshotHttpHandler(JsonSerializer.Serialize(snapshot));
    using var http = new HttpClient(handler) { BaseAddress = new Uri("http://portfolio.invalid/") };
    var reader = new PortfolioSnapshotReader(http);
    var result = await reader.Read(CancellationToken.None);
    using var evidence = JsonDocument.Parse(result);
    Check(
        evidence.RootElement.GetProperty("summary").GetProperty("currency").GetString() == "THB",
        "explicit monetary units"
    );
    Check(
        !evidence.RootElement.GetProperty("history")[0].GetProperty("available").GetBoolean(),
        "missing history is unavailable, not an empty verified ledger"
    );
    var history = JsonSerializer.Serialize(
        new
        {
            coverage = "Incomplete retained history",
            trades = Enumerable
                .Range(0, 25)
                .Select(index => new
                {
                    transactionId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    symbol = "BTC",
                    side = "buy",
                    quantity = 1m,
                    price = 100m,
                    fee = 0.25m,
                    feeCredit = 0m,
                    timestamp = "2026-10-12T00:00:00Z",
                    secret = "private",
                })
                .ToArray(),
        }
    );
    using var historyHandler = new SnapshotHttpHandler(JsonSerializer.Serialize(snapshot), history);
    using var historyHttp = new HttpClient(historyHandler) { BaseAddress = new Uri("http://portfolio.invalid/") };
    var enriched = await new PortfolioSnapshotReader(historyHttp).Read(CancellationToken.None);
    using var enrichedJson = JsonDocument.Parse(enriched);
    var retained = enrichedJson.RootElement.GetProperty("history")[0];
    Check(
        retained.GetProperty("available").GetBoolean()
            && retained.GetProperty("truncated").GetBoolean()
            && retained.GetProperty("trades").GetArrayLength() == 20,
        "bounded retained trade evidence and explicit truncation"
    );
    Check(!enriched.Contains("private", StringComparison.Ordinal), "trade field whitelist");
    Check(
        !result.Contains("private", StringComparison.Ordinal)
            && !result.Contains("credentials", StringComparison.Ordinal),
        "snapshot whitelist"
    );
    foreach (
        var body in new[]
        {
            JsonSerializer.Serialize(snapshot with { updatedAt = null }),
            JsonSerializer.Serialize(snapshot with { syncing = true }),
            JsonSerializer.Serialize(snapshot with { error = "not reconciled" }),
        }
    )
    {
        using var badHandler = new SnapshotHttpHandler(body);
        using var badHttp = new HttpClient(badHandler) { BaseAddress = new Uri("http://portfolio.invalid/") };
        try
        {
            await new PortfolioSnapshotReader(badHttp).Read(CancellationToken.None);
            throw new InvalidOperationException("unavailable snapshot accepted");
        }
        catch (InvalidOperationException error)
            when (error.Message.StartsWith("A reconciled portfolio snapshot is unavailable", StringComparison.Ordinal))
        { }
    }
}

static async Task ProcessBoundary()
{
    var key = Environment.GetEnvironmentVariable("CODEX_API_KEY");
    var brokerKey = Environment.GetEnvironmentVariable("BITKUB_API_KEY");
    var brokerSecret = Environment.GetEnvironmentVariable("BITKUB_API_SECRET");
    try
    {
        Environment.SetEnvironmentVariable("CODEX_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("BITKUB_API_KEY", "private-test-value");
        Environment.SetEnvironmentVariable("BITKUB_API_SECRET", "private-test-value");
        var worker = new CodexWorker(
            NodeRuntime.Resolve(null),
            Path.GetFullPath("tests/Team.Checks/worker-fixture.mjs")
        );
        var output = await worker.Run(
            "analyst",
            "report",
            new[] { ("ceo", "plan") },
            "snapshot",
            CancellationToken.None
        );
        Check(output == "analyst|plan|snapshot", "real stdin/stdout boundary and cleared broker environment");
        Check(
            await worker.Run("ceo", "utf8 report", Array.Empty<(string, string)>(), null, CancellationToken.None)
                == "รายงานภาษาไทย ฿100",
            "UTF8 report transport"
        );
        try
        {
            await worker.Run("ceo", "fail safely", Array.Empty<(string, string)>(), "snapshot", CancellationToken.None);
            throw new InvalidOperationException("failed worker accepted");
        }
        catch (InvalidOperationException error)
            when (error.Message.StartsWith("Codex worker failed", StringComparison.Ordinal))
        {
            Check(!error.Message.Contains("sensitive", StringComparison.Ordinal), "safe worker error");
        }

        using var cancel = new CancellationTokenSource();
        var pending = worker.Run(
            "ceo",
            "wait for cancellation",
            Array.Empty<(string, string)>(),
            "snapshot",
            cancel.Token
        );
        await cancel.CancelAsync();
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("cancelled process accepted");
        }
        catch (OperationCanceledException) { }
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_API_KEY", key);
        Environment.SetEnvironmentVariable("BITKUB_API_KEY", brokerKey);
        Environment.SetEnvironmentVariable("BITKUB_API_SECRET", brokerSecret);
    }
}

static CancellationToken TimeoutToken() => CancellationToken.None;

static void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Check failed: {name}");
    }
}
