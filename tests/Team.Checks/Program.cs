using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;
using Team.Checks;

if (args.Contains("--continuous-only", StringComparer.Ordinal))
{
    SpecialistWorkerTransportChecks.Run();
    SpecialistResearchMemoryChecks.Run();
    await ResearchBudgetChecks.Run();
    await ContinuousResearchChecks.Run();
    await ContinuousScheduleChecks.Run();
    await StaleResearchEvidenceChecks.Run();
    return;
}

if (args.Contains("--feature-sql-only", StringComparer.Ordinal))
{
    await ResearchBudgetSqlChecks.Run();
    await ContinuousResearchSqlChecks.Run();
    Console.WriteLine("Research budget SQL checks passed.");
    return;
}

if (args.Contains("--worker-cleanup-only", StringComparer.Ordinal))
{
    await WorkerProcessCleanupChecks.Run();
    return;
}

await WorkerFailureChecks.Run();
await SignalLoopChecks.Run();
await DailyLearningChecks.Run();
await AutonomousContextChecks.Run();
await NativeResearchChecks.Run();
await StartupContextChecks.Run();
await ResearchContextBudgetChecks.Run();
await ResearchBudgetChecks.Run();
SpecialistWorkerTransportChecks.Run();
SpecialistResearchMemoryChecks.Run();
await ContinuousResearchChecks.Run();
await ContinuousScheduleChecks.Run();
await StaleResearchEvidenceChecks.Run();
await ResearchBudgetSqlChecks.Run();
await ContinuousResearchSqlChecks.Run();
await NewsEvidenceChecks.Run();
await ResearchAssessmentChecks.Run();
await CompanyNewsSignalChecks.Run();
await Success();
await ResearchMemory();
await AccountScopedReviewMemory();
await ProposalPublication();
await BusyAndUnrelatedStop();
await Cancellation();
await Failure();
await MissingConfiguration();
await LateOutput();
await SnapshotWhitelist();
await ProcessBoundary();
await WorkerProcessCleanupChecks.Run();
InvalidObjective();
await DailyBriefBoundary();
await CompanyLoopChecks.Run();
await ProposalTransportChecks.Run();
await RestingEntryChecks.Run();
await RequestedEvidenceChecks.Run();
await TradingModeProposalChecks.Run();
await AdaptiveSizingChecks.Run();
Console.WriteLine("Team checks passed.");

static async Task Success()
{
    var store = new FakeTeamRunStore();
    var worker = new FakeCodexWorker(
        (role, _, _) =>
            Task.FromResult(
                new ResearchReport(
                    $"{role}-output",
                    null,
                    role == "reviewer"
                        ? new LearningLesson(
                            "Do not force entries",
                            "Unknown net PnL",
                            "Compare fixed-hour observations"
                        )
                        : null
                )
            )
    );
    await using var coordinator = await Start(store, worker);
    var run = await coordinator.Start("objective", CancellationToken.None);
    var result = await store.WaitFor(
        run.Id,
        item => item.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
        TimeoutToken()
    );
    Check(result.Status == TeamRunStatus.Completed, "team pipeline completed: " + result.Error);
    Check(
        worker.Calls.Count == 3
            && worker.Calls[1].Previous[0].Output == "ceo-output"
            && worker.Calls[2].Previous[1].Output == "analyst-output",
        "ordered handoff"
    );
    Check(result.Steps.All(step => step.Status == TeamRunStatus.Completed), "success status");
    Check(result.AccountKey == new string('a', 64), "run account key persisted");
    Check(worker.Calls.All(call => call.Portfolio is not null), "portfolio snapshot");
    Check(result.Learning?.Markets.Count == 1, "learning evidence durably retained with completed report");
    Check(
        result.Lesson?.Lesson == "Do not force entries" && result.Decision == LearningDecision.NoProposal,
        "structured lesson and proposal decision persisted"
    );
    using var context = JsonDocument.Parse(
        worker.Calls[0].Portfolio ?? throw new InvalidOperationException("context missing")
    );
    var mandate = context.RootElement.GetProperty("mandate");
    Check(
        mandate.GetProperty("dailyNetProfitTargetThb").GetDecimal() == 500m
            && mandate.GetProperty("calendarTimeZone").GetString() == "Asia/Bangkok"
            && mandate.GetProperty("acceptFullCapitalLoss").GetBoolean(),
        "central owner mandate reaches worker"
    );
    var next = await coordinator.Start("next learning round", CancellationToken.None);
    await store.WaitFor(next.Id, item => item.Status == TeamRunStatus.Completed, TimeoutToken());
    using var recalled = JsonDocument.Parse(
        worker.Calls[3].Portfolio ?? throw new InvalidOperationException("learning context missing")
    );
    var days = recalled.RootElement.GetProperty("dailyLearning").GetProperty("days");
    Check(
        days.GetArrayLength() == 1
            && days[0].GetProperty("rounds")[0].GetProperty("runId").GetGuid() == result.Id
            && days[0].GetProperty("rounds")[0].GetProperty("lesson").GetProperty("Lesson").GetString()
                == "Do not force entries",
        "completed report memory reaches the next CEO round"
    );
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
            null,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
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

static async Task AccountScopedReviewMemory()
{
    var store = new FakeTeamRunStore();
    var account = new string('a', 64);
    var other = new string('b', 64);
    var before = DateTimeOffset.UtcNow;
    var valid = new TeamRun(
        Guid.NewGuid(),
        "valid",
        TeamRunStatus.Completed,
        before.AddMinutes(-3),
        before.AddMinutes(-3),
        [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, "valid-review", null)],
        null,
        account
    );
    var failedLatest = valid with
    {
        Id = Guid.NewGuid(),
        CreatedAt = before.AddMinutes(-1),
        UpdatedAt = before.AddMinutes(-1),
        Status = TeamRunStatus.Failed,
        Steps = [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Failed, null, "failed")],
    };
    await store.Save(valid, CancellationToken.None);
    await store.Save(failedLatest, CancellationToken.None);
    await store.Save(valid with { Id = Guid.NewGuid(), AccountKey = other }, CancellationToken.None);
    await store.Save(valid with { Id = Guid.NewGuid(), AccountKey = null }, CancellationToken.None);
    Check(
        (await store.LatestReviewed(account, before, CancellationToken.None))?.Id == valid.Id,
        "failed latest preserves last valid review"
    );
    Check(
        await store.LatestReviewed(other, before, CancellationToken.None) is not null
            && await store.LatestReviewed(account, before, CancellationToken.None) is not null,
        "other account remains isolated"
    );
    Check(
        await store.LatestReviewed("c" + new string('a', 63), before, CancellationToken.None) is null,
        "legacy unknown account excluded"
    );
}

static async Task ProposalPublication()
{
    var proposal = new TradeProposal(
        "BTC",
        TradeProposalSide.Buy,
        "0.01",
        "100",
        "evidence",
        FakePortfolioSnapshotReader.FetchedAt
    );
    var publisher = new FakeOrderProposalPublisher();
    var worker = new FakeCodexWorker(
        (role, _, _) => Task.FromResult(new ResearchReport(role, role == "reviewer" ? proposal : null))
    );
    var store = new FakeTeamRunStore();
    await using var coordinator = await Start(
        store,
        worker,
        publisher,
        new FakePortfolioSnapshotReader { TradingMode = "manualReview" }
    );
    var run = await coordinator.Start("proposal", CancellationToken.None);
    var result = await store.WaitFor(
        run.Id,
        item => item.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
        TimeoutToken()
    );
    Check(result.Status == TeamRunStatus.Completed, "proposal pipeline completed: " + result.Error);
    Check(publisher.Calls.Count == 1 && publisher.Calls[0].RunId == run.Id, "reviewer proposal publishes once");
    Check(result.AccountKey == new string('a', 64), "proposal account binding");

    var rejectedPublisher = new FakeOrderProposalPublisher();
    var analystWorker = new FakeCodexWorker(
        (role, _, _) => Task.FromResult(new ResearchReport(role, role == "analyst" ? proposal : null))
    );
    var rejectedStore = new FakeTeamRunStore();
    await using var rejected = await Start(
        rejectedStore,
        analystWorker,
        rejectedPublisher,
        new FakePortfolioSnapshotReader { TradingMode = "manualReview" }
    );
    var rejectedRun = await rejected.Start("bad proposal", CancellationToken.None);
    var rejectedResult = await rejectedStore.WaitFor(
        rejectedRun.Id,
        item => item.Status == TeamRunStatus.Failed,
        TimeoutToken()
    );
    Check(
        rejectedPublisher.Calls.Count == 0 && rejectedResult.Status == TeamRunStatus.Failed,
        "analyst proposal rejected"
    );

    publisher.Fail = true;
    var failingStore = new FakeTeamRunStore();
    await using var failing = await Start(
        failingStore,
        worker,
        publisher,
        new FakePortfolioSnapshotReader { TradingMode = "manualReview" }
    );
    var failingRun = await failing.Start("publisher failure", CancellationToken.None);
    var failed = await failingStore.WaitFor(failingRun.Id, item => item.Status == TeamRunStatus.Failed, TimeoutToken());
    Check(failed.Status == TeamRunStatus.Failed, "publisher failure visible");

    var walletObservedAt = DateTimeOffset.UtcNow;
    var context =
        $"{{\"evidence\":{{\"accountKey\":\"{new string('a', 64)}\",\"walletEvidence\":{{\"accountKey\":\"{new string('a', 64)}\",\"observedAt\":\"{walletObservedAt:O}\",\"balances\":[{{\"symbol\":\"THB\",\"quantity\":1000,\"available\":700}},{{\"symbol\":\"BTC\",\"quantity\":1,\"available\":1}}]}},\"marketResearch\":{{\"available\":true,\"fetchedAt\":\"{FakePortfolioSnapshotReader.FetchedAt:O}\",\"markets\":[{{\"symbol\":\"BTC_THB\",\"last\":100,\"bid\":99.9,\"ask\":100.1}}]}},\"summary\":{{\"availableThb\":200}},\"positions\":[{{\"symbol\":\"BTC\",\"quantity\":1}}]}}}}";
    TradeProposalValidator.Validate(proposal, context);
    foreach (
        var invalidContext in new[]
        {
            context.Replace(
                $"\"walletEvidence\":{{\"accountKey\":\"{new string('a', 64)}\"",
                $"\"walletEvidence\":{{\"accountKey\":\"{new string('b', 64)}\"",
                StringComparison.Ordinal
            ),
            context.Replace(
                $"\"observedAt\":\"{walletObservedAt:O}\"",
                $"\"observedAt\":\"{walletObservedAt.AddMinutes(-11):O}\"",
                StringComparison.Ordinal
            ),
            context.Replace("\"available\":700", "\"available\":null", StringComparison.Ordinal),
        }
    )
    {
        var rejectedWallet = false;
        try
        {
            TradeProposalValidator.Validate(proposal, invalidContext);
        }
        catch (InvalidOperationException)
        {
            rejectedWallet = true;
        }

        Check(rejectedWallet, "wallet evidence rejects stale, wrong-account or unavailable cash");
    }

    var reservedContext = context.Replace("\"available\":700", "\"available\":0", StringComparison.Ordinal);
    var reservedRejected = false;
    try
    {
        TradeProposalValidator.Validate(proposal with { Amount = "0.01" }, reservedContext);
    }
    catch (InvalidOperationException)
    {
        reservedRejected = true;
    }

    Check(reservedRejected, "wallet evidence never spends reserved cash");
    foreach (
        var invalid in new[]
        {
            proposal with
            {
                Symbol = "ETH",
            },
            proposal with
            {
                EvidenceAt = FakePortfolioSnapshotReader.FetchedAt.AddMinutes(-11),
            },
            proposal with
            {
                Amount = "701",
            },
        }
    )
    {
        var rejectedProposal = false;
        try
        {
            TradeProposalValidator.Validate(invalid, context);
        }
        catch (InvalidOperationException)
        {
            rejectedProposal = true;
        }

        Check(rejectedProposal, "invalid proposal rejected");
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
    var accountKey = new string('a', 64);
    var own = result[0] with { AccountKey = accountKey };
    await store.Save(own, CancellationToken.None);
    await store.Save(result[1] with { AccountKey = new string('b', 64) }, CancellationToken.None);
    var account = new FakeCurrentAccountReader(accountKey);
    var scoped = await new GetDailyBriefHandler(store, account).Handle(
        new GetDailyBriefQuery(day),
        CancellationToken.None
    );
    Check(scoped.Count == 1 && scoped[0].Id == own.Id, "daily brief excludes other and legacy accounts");
    var latest = await new GetTeamHandler(store, account).Handle(new GetTeamQuery(), CancellationToken.None);
    Check(latest?.Id == own.Id, "overview selects latest current-account run");
}

static TeamRun RunAt(Guid id, DateTimeOffset createdAt)
{
    return new TeamRun(id, "brief", TeamRunStatus.Completed, createdAt, createdAt, [], null);
}

static async Task<TeamCoordinatorLease> Start(
    FakeTeamRunStore store,
    FakeCodexWorker worker,
    IOrderProposalPublisher? publisher = null,
    IPortfolioSnapshotReader? snapshotReader = null
)
{
    var coordinator = new TeamCoordinator(
        store,
        worker,
        snapshotReader ?? new FakePortfolioSnapshotReader(),
        publisher
    );
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
    var planReason = "Pullback\nTarget (plan): THB 105\nInvalidation (plan): THB 95";
    string[] orderStates = ["pending", "submitted", "partiallyFilled", "unknown", "filled"];
    var activeOrders = JsonSerializer.Serialize(
        new
        {
            proposals = orderStates
                .Select(status => new
                {
                    id = Guid.NewGuid(),
                    symbol = "BTC",
                    side = "buy",
                    amount = "100.01",
                    limitPrice = "99.99",
                    status,
                    reason = planReason,
                    secret = "private",
                })
                .ToArray(),
        }
    );
    using var ordersHandler = new SnapshotHttpHandler(JsonSerializer.Serialize(snapshot), orders: activeOrders);
    using var ordersHttp = new HttpClient(ordersHandler) { BaseAddress = new Uri("http://portfolio.invalid/") };
    var ordersContext = await new PortfolioSnapshotReader(ordersHttp).Read(CancellationToken.None);
    using var ordersJson = JsonDocument.Parse(ordersContext);
    var openOrders = ordersJson.RootElement.GetProperty("openOrders");
    Check(
        openOrders.GetProperty("available").GetBoolean()
            && !openOrders.GetProperty("truncated").GetBoolean()
            && openOrders.GetProperty("orders").GetArrayLength() == 4
            && openOrders.GetProperty("orders")[0].GetProperty("amount").GetString() == "100.01"
            && openOrders.GetProperty("orders")[0].GetProperty("reason").GetString() == planReason,
        "active proposal states and exact immutable entry plan reach research"
    );
    Check(!ordersContext.Contains("private", StringComparison.Ordinal), "open order whitelist");
    using var missingOrdersHandler = new SnapshotHttpHandler(JsonSerializer.Serialize(snapshot), orders: "{}");
    using var missingOrdersHttp = new HttpClient(missingOrdersHandler)
    {
        BaseAddress = new Uri("http://portfolio.invalid/"),
    };
    using var missingOrders = JsonDocument.Parse(
        await new PortfolioSnapshotReader(missingOrdersHttp).Read(CancellationToken.None)
    );
    Check(
        !missingOrders.RootElement.GetProperty("openOrders").GetProperty("available").GetBoolean(),
        "unavailable order evidence is never an empty verified queue"
    );
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
        var typed = await worker.Report("reviewer", "typed lesson", [], null, CancellationToken.None);
        var preflightWorker = new CodexWorker(
            Path.GetFullPath("tests/Team.Checks/worker-fixture.mjs"),
            Path.GetFullPath("tests/Team.Checks/worker-fixture.mjs")
        );
        try
        {
            await preflightWorker.Report("ceo", "oversized wire", [], new string('"', 60000), CancellationToken.None);
            throw new InvalidOperationException("Oversized wire request was accepted.");
        }
        catch (CodexWorkerException error) when (error.Code == "worker_input_too_large") { }

        Check(
            await worker.Run(
                "reviewer",
                "utf8 transport size",
                [("ceo", new string('ก', 6000)), ("analyst", new string('ข', 6000))],
                new string('ค', 20000),
                CancellationToken.None
            ) == "UTF8 size accepted",
            "Thai context respects worker input budget without Unicode expansion"
        );
        Check(typed.Lesson?.Lesson == "Keep baseline", "typed reviewer lesson crosses process boundary");
        try
        {
            await worker.Report("reviewer", "missing lesson", [], null, CancellationToken.None);
            throw new InvalidOperationException("Reviewer null lesson accepted");
        }
        catch (CodexWorkerException error) when (error.Code == "worker_response_invalid") { }

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
