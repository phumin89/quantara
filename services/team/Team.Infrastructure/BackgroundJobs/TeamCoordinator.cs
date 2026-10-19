using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class TeamCoordinator : BackgroundService, ITeamCoordinator
{
    private readonly Channel<Guid> queue = Channel.CreateBounded<Guid>(1);

    private readonly ITeamRunStore store;

    private readonly ICodexWorker worker;

    private readonly IPortfolioSnapshotReader portfolio;

    private readonly IOrderProposalPublisher? proposals;

    private readonly ICurrentAccountReader? account;

    private readonly INewsEvidenceReader? news;

    private readonly IResearchBudget? budget;

    private readonly SemaphoreSlim gate = new(1, 1);

    private TeamRun? activeRun;

    private CancellationTokenSource? activeCancellation;

    private bool ready;

    private readonly TaskCompletionSource initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TeamCoordinator(
        ITeamRunStore store,
        ICodexWorker worker,
        IPortfolioSnapshotReader portfolio,
        IOrderProposalPublisher? proposals = null,
        ICurrentAccountReader? account = null,
        INewsEvidenceReader? news = null,
        IResearchBudget? budget = null
    )
    {
        this.store = store;
        this.worker = worker;
        this.portfolio = portfolio;
        this.proposals = proposals;
        this.account = account;
        this.news = news;
        this.budget = budget;
    }

    public bool IsConfigured => this.worker.IsConfigured && this.ready;

    public Task Initialization => this.initialization.Task;

    public async Task<TeamRun> Start(string objective, CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            if (!this.IsConfigured)
            {
                throw new TeamUnavailableException();
            }

            if (this.activeRun is not null)
            {
                throw new TeamBusyException();
            }

            var accountKey = this.account is null ? null : await this.account.Read(cancellationToken);
            if (
                accountKey is not null
                && (accountKey.Length != 64 || accountKey.Any(character => !Uri.IsHexDigit(character)))
            )
            {
                throw new InvalidOperationException("Verified starting account is invalid.");
            }

            var now = DateTimeOffset.UtcNow;
            if (this.budget is not null)
            {
                using var scope = JsonDocument.Parse(await this.portfolio.Read(cancellationToken));
                var verifiedAccount = scope.RootElement.GetProperty("accountKey").GetString();
                if (verifiedAccount != accountKey || accountKey is null)
                {
                    throw new InvalidDataException("Research budget account changed.");
                }

                var hasPosition = ResearchEvidence.HeldSymbols(scope.RootElement).Count > 0;
                now = DateTimeOffset.UtcNow;
                var claim = await this.budget.TryClaim(accountKey, hasPosition, now, cancellationToken);
                if (!claim.Allowed)
                {
                    throw new ResearchBudgetDeferredException(claim);
                }
            }

            var run = new TeamRun(
                Guid.NewGuid(),
                objective,
                TeamRunStatus.Queued,
                now,
                now,
                Enum.GetValues<TeamRole>()
                    .Select(role => new TeamStep(role, TeamRunStatus.Queued, null, null))
                    .ToArray(),
                null,
                AccountKey: accountKey
            );
            await this.store.Save(run, cancellationToken);
            this.activeRun = run;
            this.activeCancellation = new CancellationTokenSource();
            if (!this.queue.Writer.TryWrite(run.Id))
            {
                throw new TeamBusyException();
            }

            return run;
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task<TeamRun?> RequestStop(Guid id, CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            if (this.activeRun?.Id != id)
            {
                return await this.store.Find(id, cancellationToken);
            }

            var run = this.activeRun;
            if (run.Status is TeamRunStatus.Completed or TeamRunStatus.Failed or TeamRunStatus.Cancelled)
            {
                return run;
            }

            this.activeCancellation?.Cancel();
            var stopped = run with
            {
                Status = TeamRunStatus.Cancelled,
                UpdatedAt = DateTimeOffset.UtcNow,
                Steps = run
                    .Steps.Select(step =>
                        step.Status is TeamRunStatus.Queued or TeamRunStatus.Running
                            ? step with
                            {
                                Status = TeamRunStatus.Cancelled,
                            }
                            : step
                    )
                    .ToArray(),
            };
            await this.store.Save(stopped, cancellationToken);
            this.activeRun = stopped;

            return stopped;
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task RequestStopCurrent(CancellationToken cancellationToken)
    {
        Guid? id;
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            id = this.activeRun?.Id;
        }
        finally
        {
            this.gate.Release();
        }

        if (id.HasValue)
        {
            await this.RequestStop(id.Value, cancellationToken);
        }
    }

    public override void Dispose()
    {
        this.activeCancellation?.Dispose();
        this.gate.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && !this.ready)
        {
            try
            {
                await this.store.Recover(stoppingToken);
                this.ready = true;
                this.initialization.TrySetResult();
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        await foreach (var id in this.queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                this.activeCancellation?.Token ?? stoppingToken
            );
            try
            {
                await this.ExecuteRun(id, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                await this.Finish(id, TeamRunStatus.Cancelled, null);
            }
            catch (CodexWorkerException error)
            {
                await this.Finish(id, TeamRunStatus.Failed, error.Message);
            }
            catch (Exception)
            {
                await this.Finish(
                    id,
                    TeamRunStatus.Failed,
                    "The team task failed. Check the portfolio snapshot, Codex authentication and server runtime, then retry."
                );
            }
            finally
            {
                await this.gate.WaitAsync(CancellationToken.None);
                try
                {
                    // A failed persistence write keeps the slot occupied until recovery.
                    if (
                        this.activeRun?.Status
                        is TeamRunStatus.Completed
                            or TeamRunStatus.Cancelled
                            or TeamRunStatus.Failed
                    )
                    {
                        this.activeRun = null;
                        this.activeCancellation?.Dispose();
                        this.activeCancellation = null;
                    }
                }
                finally
                {
                    this.gate.Release();
                }
            }
        }
    }

    private async Task ExecuteRun(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await this.portfolio.Read(cancellationToken);
        using var snapshotDocument = JsonDocument.Parse(snapshot);
        var accountKey = snapshotDocument.RootElement.TryGetProperty("accountKey", out var account)
            ? account.GetString()
            : null;
        if (accountKey is null || accountKey.Length != 64 || accountKey.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("Portfolio snapshot account key is invalid.");
        }

        var manualReview =
            snapshotDocument.RootElement.TryGetProperty("tradingMode", out var mode)
            && mode.ValueKind == JsonValueKind.String
            && mode.GetString() == "manualReview";

        var current =
            await this.store.Find(id, cancellationToken) ?? throw new InvalidOperationException("Run missing.");
        if (current.AccountKey is { } startingAccount && startingAccount != accountKey)
        {
            throw new InvalidOperationException("Portfolio account changed after the research job was queued.");
        }

        await this.gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.activeRun?.Id != id || this.activeRun.Status == TeamRunStatus.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            current = this.activeRun with { AccountKey = accountKey, Learning = DailyLearning.Capture(snapshot) };
            await this.store.Save(current, cancellationToken);
            this.activeRun = current;
        }
        finally
        {
            this.gate.Release();
        }

        var prior = await this.store.LatestReviewed(accountKey, current.CreatedAt, cancellationToken);
        var learningHistory = await this.store.LearningHistory(accountKey, current.CreatedAt, cancellationToken);
        var evaluatedHistory = new List<TeamRun>();
        foreach (var previousRun in learningHistory)
        {
            var evaluation = DailyLearning.Evaluate(previousRun, learningHistory, current);
            var evaluated = previousRun with { LearningEvaluation = evaluation };
            if (previousRun.LearningEvaluation is null && evaluation is not null)
            {
                await this.store.Save(evaluated, cancellationToken);
            }

            evaluatedHistory.Add(evaluated);
        }

        var rawEvidence = snapshot;
        var newsEvidence = this.news is null ? null : await this.news.Read(null, cancellationToken);
        var previous = new List<(string Role, string Output)>();
        foreach (var role in Enum.GetValues<TeamRole>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = TeamResearchContext.Build(
                role.ToString().ToLowerInvariant(),
                current.Objective,
                previous,
                rawEvidence,
                current,
                evaluatedHistory,
                prior,
                newsEvidence
            );
            var run = await this.UpdateStep(id, role, TeamRunStatus.Running, null, cancellationToken);
            var report = await this.worker.Report(
                role.ToString().ToLowerInvariant(),
                run.Objective,
                previous,
                snapshot,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            if (report.Proposal is not null && !manualReview)
            {
                report = report with
                {
                    Proposal = null,
                    Output =
                        report.Output
                        + "\n\nข้อเสนอถูกข้าม: โหมดปัจจุบันเป็นการวิจัยเท่านั้น การสร้างข้อเสนอรออนุมัติต้องอยู่ใน ManualReview",
                };
            }

            if (report.Assessment is not null)
            {
                ResearchAssessmentValidator.Validate(report.Assessment, run, accountKey, snapshot);
            }

            await this.UpdateStep(id, role, TeamRunStatus.Completed, report.Output, cancellationToken, report);
            previous.Add((role.ToString().ToLowerInvariant(), report.Output));
            if (report.ResearchSymbols is { Count: > 0 } requested)
            {
                if (role != TeamRole.Ceo)
                {
                    throw new CodexWorkerException("worker_response_invalid");
                }

                using var originalContext = JsonDocument.Parse(snapshot);
                var research = originalContext.RootElement.GetProperty("evidence").GetProperty("marketResearch");
                if (
                    !research.TryGetProperty("scan", out var scan)
                    || !scan.TryGetProperty("symbols", out var universe)
                    || requested.Count > 3
                    || requested.Distinct(StringComparer.Ordinal).Count() != requested.Count
                    || requested.Any(symbol =>
                        !universe.EnumerateArray().Any(item => item.GetString() == symbol + "_THB")
                    )
                )
                {
                    throw new CodexWorkerException("worker_response_invalid");
                }

                using var refreshed = JsonDocument.Parse(
                    await this.portfolio.ReadForMarkets(requested, cancellationToken)
                );
                if (refreshed.RootElement.GetProperty("accountKey").GetString() != accountKey)
                {
                    throw new InvalidOperationException("Account changed while collecting requested market evidence.");
                }

                manualReview =
                    refreshed.RootElement.TryGetProperty("tradingMode", out var refreshedMode)
                    && refreshedMode.ValueKind == JsonValueKind.String
                    && refreshedMode.GetString() == "manualReview";

                rawEvidence = refreshed.RootElement.GetRawText();
                if (this.news is not null)
                {
                    newsEvidence = await this.news.Read(string.Join(',', requested), cancellationToken);
                }
            }

            if (report.Proposal is not null)
            {
                if (role != TeamRole.Reviewer || this.proposals is null)
                {
                    throw new InvalidOperationException("Trade proposal publishing is unavailable for this role.");
                }

                TradeProposalValidator.Validate(report.Proposal, snapshot);
                await this.proposals.Publish(id, accountKey, report.Proposal, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await this.Finish(id, TeamRunStatus.Completed, null);
    }

    private async Task<TeamRun> UpdateStep(
        Guid id,
        TeamRole role,
        TeamRunStatus status,
        string? output,
        CancellationToken cancellationToken,
        ResearchReport? report = null
    )
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = this.activeRun ?? throw new InvalidOperationException("Team task is missing.");
            if (current.Id != id || current.Status == TeamRunStatus.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var run = current with
            {
                Status = TeamRunStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow,
                Lesson = role == TeamRole.Reviewer && report is not null ? report.Lesson : current.Lesson,
                Assessment = role == TeamRole.Reviewer && report is not null ? report.Assessment : current.Assessment,
                Decision =
                    role == TeamRole.Reviewer && report is not null
                        ? report.Proposal?.Side switch
                        {
                            TradeProposalSide.Buy => LearningDecision.BuyProposal,
                            TradeProposalSide.Sell => LearningDecision.SellProposal,
                            _ => LearningDecision.NoProposal,
                        }
                        : current.Decision,
                Steps = current
                    .Steps.Select(step => step.Role == role ? step with { Status = status, Output = output } : step)
                    .ToArray(),
            };
            await this.store.Save(run, cancellationToken);
            this.activeRun = run;

            return run;
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task Finish(Guid id, TeamRunStatus status, string? error)
    {
        await this.gate.WaitAsync(CancellationToken.None);
        try
        {
            var current = this.activeRun;
            if (current is null || current.Id != id || current.Status == TeamRunStatus.Cancelled)
            {
                return;
            }

            var run = current with
            {
                Status = status,
                Error = error,
                UpdatedAt = DateTimeOffset.UtcNow,
                Steps = current
                    .Steps.Select(step =>
                        step.Status is TeamRunStatus.Queued or TeamRunStatus.Running
                            ? step with
                            {
                                Status =
                                    status == TeamRunStatus.Failed && step.Status == TeamRunStatus.Running
                                        ? TeamRunStatus.Failed
                                        : TeamRunStatus.Cancelled,
                                Error = step.Status == TeamRunStatus.Running ? error : null,
                            }
                            : step
                    )
                    .ToArray(),
            };
            await this.store.Save(run, CancellationToken.None);
            this.activeRun = run;
        }
        finally
        {
            this.gate.Release();
        }
    }
}
