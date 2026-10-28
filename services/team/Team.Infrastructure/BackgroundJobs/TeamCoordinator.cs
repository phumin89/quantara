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

    private readonly TimeProvider time;

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
        IResearchBudget? budget = null,
        TimeProvider? time = null
    )
    {
        this.store = store;
        this.worker = worker;
        this.portfolio = portfolio;
        this.proposals = proposals;
        this.account = account;
        this.news = news;
        this.budget = budget;
        this.time = time ?? TimeProvider.System;
    }

    public bool IsConfigured => this.worker.IsConfigured && this.ready;

    public Task Initialization => this.initialization.Task;

    public Task<TeamRun> Start(string objective, CancellationToken cancellationToken) =>
        this.StartCore(objective, false, false, cancellationToken);

    public Task<TeamRun> StartAutomatic(
        string objective,
        CancellationToken cancellationToken,
        bool marketTriggered = false
    ) => this.StartCore(objective, true, marketTriggered, cancellationToken);

    private async Task<TeamRun> StartCore(
        string objective,
        bool automatic,
        bool marketTriggered,
        CancellationToken cancellationToken
    )
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

            var now = this.time.GetUtcNow();
            ResearchTask? task = null;
            if (automatic || this.budget is not null)
            {
                using var scope = JsonDocument.Parse(await this.portfolio.Read(cancellationToken));
                var verifiedAccount = scope.RootElement.GetProperty("accountKey").GetString();
                if (accountKey is not null && verifiedAccount != accountKey)
                {
                    throw new InvalidDataException("Research budget account changed.");
                }

                accountKey = verifiedAccount;
                if (accountKey is null || accountKey.Length != 64 || !accountKey.All(Uri.IsHexDigit))
                {
                    throw new InvalidDataException("Research account is invalid.");
                }

                now = this.time.GetUtcNow();
                if (automatic)
                {
                    var news = await this.ReadNews(cancellationToken);
                    now = this.time.GetUtcNow();
                    var history = await this.store.RecentResearch(accountKey, now, cancellationToken);
                    now = this.time.GetUtcNow();
                    var candidates = TeamResearchRouter.Candidates(
                        scope.RootElement,
                        news,
                        history,
                        now,
                        marketTriggered
                    );
                    ResearchBudgetDecision? deferred = null;
                    var attemptedLanes = new HashSet<bool>();
                    foreach (var candidate in candidates)
                    {
                        var priority = candidate.Specialist == TeamRole.Positions;
                        if (!attemptedLanes.Add(priority))
                        {
                            continue;
                        }

                        var claim = this.budget is null
                            ? new ResearchBudgetDecision(true, null, "unconfigured_test_budget")
                            : await this.budget.TryClaim(accountKey, priority, now, cancellationToken);
                        if (claim.Allowed)
                        {
                            task = candidate;
                            break;
                        }

                        if (deferred is null || claim.NextAllowedAt < deferred.NextAllowedAt)
                        {
                            deferred = claim with { NextRole = candidate.Specialist };
                        }
                    }

                    if (task is null)
                    {
                        throw new ResearchBudgetDeferredException(
                            deferred ?? new(false, now.AddMinutes(15), "waiting_for_evidence")
                        );
                    }
                }
                else if (this.budget is not null)
                {
                    var claim = await this.budget.TryClaim(accountKey, false, now, cancellationToken);
                    if (!claim.Allowed)
                    {
                        throw new ResearchBudgetDeferredException(claim);
                    }
                }
            }

            TeamRole[] roles = task is null
                ? [TeamRole.Ceo, TeamRole.Analyst, TeamRole.Reviewer]
                : [task.Specialist, TeamRole.Reviewer];
            var run = new TeamRun(
                Guid.NewGuid(),
                objective,
                TeamRunStatus.Queued,
                now,
                now,
                roles.Select(role => new TeamStep(role, TeamRunStatus.Queued, null, null)).ToArray(),
                null,
                AccountKey: accountKey,
                Task: task
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
                UpdatedAt = this.time.GetUtcNow(),
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

            if (
                this.activeRun.Task is { } queuedTask
                && queuedTask.PolicyHash != ResearchEvidence.PolicyHash(snapshotDocument.RootElement)
            )
            {
                throw new CodexWorkerException("worker_evidence_changed");
            }

            current = this.activeRun with
            {
                AccountKey = accountKey,
                Learning = DailyLearning.Capture(snapshot),
                Task = this.activeRun.Task is { } focused
                    ? focused with
                    {
                        InputDigest = TeamResearchRouter.Digest(snapshot),
                        EvidenceAt = this.time.GetUtcNow(),
                    }
                    : null,
            };
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
        var newsEvidence = current.Task is null
            ? this.news is null
                ? null
                : await this.news.Read(null, cancellationToken)
            : await this.ReadNews(cancellationToken);
        var previous = new List<(string Role, string Output)>();
        foreach (var role in current.Steps.Select(step => step.Role))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scopedEvidence = current.Task is null ? rawEvidence : TeamResearchScope.Project(role, rawEvidence);
            snapshot = TeamResearchContext.Build(
                role.ToString().ToLowerInvariant(),
                current.Objective,
                previous,
                scopedEvidence,
                current,
                evaluatedHistory,
                prior,
                newsEvidence
            );
            var digest = TeamResearchRouter.Digest(
                CodexWorker.SerializeRequest(role.ToString().ToLowerInvariant(), current.Objective, previous, snapshot)
            );
            var run = await this.UpdateStep(
                id,
                role,
                TeamRunStatus.Running,
                null,
                cancellationToken,
                inputDigest: digest,
                modelAudit: this.worker.ModelSelection(role.ToString().ToLowerInvariant())
            );
            ResearchReport report;
            try
            {
                report = await this.worker.Report(
                    role.ToString().ToLowerInvariant(),
                    run.Objective,
                    previous,
                    snapshot,
                    cancellationToken
                );
            }
            catch (CodexWorkerException error)
            {
                await this.RecordAudit(id, role, error.Usage, error.ModelAudit);
                throw;
            }

            await this.RecordAudit(id, role, report.Usage, report.ModelAudit);
            cancellationToken.ThrowIfCancellationRequested();
            if (run.Task is not null)
            {
                await this.VerifyCurrentEvidence(run, rawEvidence, cancellationToken);
            }

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

            report = PolicyCandidateValidation.Validate(report, role, run, accountKey, snapshot, this.time.GetUtcNow());

            await this.UpdateStep(id, role, TeamRunStatus.Completed, report.Output, cancellationToken, report);
            previous.Add((role.ToString().ToLowerInvariant(), report.Output));
            if (report.ResearchSymbols is { Count: > 0 } requested)
            {
                if (role is not (TeamRole.Ceo or TeamRole.Intraday))
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
                    if (run.Task is null)
                    {
                        throw new InvalidOperationException(
                            "Account changed while collecting requested market evidence."
                        );
                    }

                    throw new CodexWorkerException("worker_evidence_changed");
                }

                if (run.Task is not null)
                {
                    using var originalEvidence = JsonDocument.Parse(rawEvidence);
                    if (
                        this.time.GetUtcNow() >= run.Task.ExpiresAt
                        || ResearchEvidence.PolicyHash(refreshed.RootElement) != run.Task.PolicyHash
                        || ResearchEvidence.HoldingsDigest(refreshed.RootElement)
                            != ResearchEvidence.HoldingsDigest(originalEvidence.RootElement)
                        || ResearchEvidence.Mode(refreshed.RootElement)
                            != ResearchEvidence.Mode(originalEvidence.RootElement)
                    )
                    {
                        throw new CodexWorkerException("worker_evidence_changed");
                    }
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
        ResearchReport? report = null,
        string? inputDigest = null,
        ResearchModelAudit? modelAudit = null
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
                UpdatedAt = this.time.GetUtcNow(),
                Lesson = role == TeamRole.Reviewer && report is not null ? report.Lesson : current.Lesson,
                Assessment = role == TeamRole.Reviewer && report is not null ? report.Assessment : current.Assessment,
                PolicyCandidate =
                    role == TeamRole.Reviewer && report is not null ? report.PolicyCandidate : current.PolicyCandidate,
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
                    .Steps.Select(step =>
                        step.Role == role
                            ? step with
                            {
                                Status = status,
                                Output = output,
                                StartedAt = status == TeamRunStatus.Running ? this.time.GetUtcNow() : step.StartedAt,
                                CompletedAt =
                                    status == TeamRunStatus.Completed ? this.time.GetUtcNow() : step.CompletedAt,
                                InputDigest = inputDigest ?? step.InputDigest,
                                Usage = report?.Usage ?? step.Usage,
                                ModelAudit = modelAudit ?? step.ModelAudit ?? report?.ModelAudit,
                            }
                            : step
                    )
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

    private ResearchReport ValidatePolicyCandidate(
        ResearchReport report,
        TeamRole role,
        TeamRun run,
        string accountKey,
        string snapshot
    )
    {
        var candidate = report.PolicyCandidate;
        if (candidate is null)
        {
            return report;
        }

        using var snapshotDocument = JsonDocument.Parse(snapshot);
        var evidenceRoot = snapshotDocument.RootElement.TryGetProperty("evidence", out var evidence)
            ? evidence
            : snapshotDocument.RootElement;
        var currentPolicyHash = evidenceRoot.TryGetProperty("policyHash", out var hash) ? hash.GetString() : null;
        var aiPolicyEnabled =
            evidenceRoot.TryGetProperty("aiPolicy", out var aiPolicy)
            && aiPolicy.ValueKind == JsonValueKind.Object
            && aiPolicy.TryGetProperty("enabled", out var enabled)
            && enabled.ValueKind == JsonValueKind.True
            && aiPolicy.TryGetProperty("effectiveRulesHash", out var effectiveHash)
            && effectiveHash.GetString() == currentPolicyHash;
        var assessment = report.Assessment;
        var assessmentEvidence = assessment?.EvidenceIds ?? [];
        var valid =
            role == TeamRole.Reviewer
            && aiPolicyEnabled
            && assessment is not null
            && assessment.SchemaVersion == "research-assessment-v1"
            && assessment.AccountKey == accountKey
            && assessment.RunId == run.Id
            && assessment.PolicyHash == currentPolicyHash
            && candidate.RunId == run.Id
            && candidate.AccountKey == accountKey
            && currentPolicyHash is not null
            && candidate.BaselineRulesHash == currentPolicyHash
            && candidate.ExpiresAt > candidate.AsOf
            && candidate.AsOf <= this.time.GetUtcNow()
            && candidate.ExpiresAt > this.time.GetUtcNow()
            && candidate.AsOf >= run.CreatedAt
            && candidate.ExpiresAt <= candidate.AsOf.AddMinutes(30)
            && candidate.EvidenceIds.Count is >= 1 and <= 16
            && candidate.EvidenceIds.Distinct(StringComparer.Ordinal).Count() == candidate.EvidenceIds.Count
            && candidate.EvidenceIds.All(id =>
                id.Length is > 0 and <= 256 && assessmentEvidence.Contains(id, StringComparer.Ordinal)
            )
            && candidate.Parameters.MaxPositions is >= 1 and <= 16
            && candidate.Parameters.MaxSpreadBps is >= 0 and <= 1000
            && candidate.Parameters.SlippageBps is >= 0 and <= 1000
            && candidate.Parameters.TakeProfitBps is > 0 and <= 10000
            && candidate.Parameters.StopLossBps is > 0 and <= 10000
            && candidate.Parameters.TrailingStopBps is > 0 and <= 10000
            && candidate.Parameters.VolumeRatio is >= 1 and <= 10
            && candidate.Parameters.TimeExitMinutes is >= 1 and <= 1440
            && candidate.Parameters.CooldownMinutes is >= 0 and <= 1440
            && candidate.Parameters.OrderLifetimeMinutes is >= 1 and <= 1440
            && candidate.Parameters.MaximumExposureFraction is > 0 and <= 1
            && candidate.Parameters.MaximumConcentrationFraction is > 0 and <= 1
            && candidate.Parameters.RiskToStopFraction is > 0 and <= 1
            && candidate.Parameters.DailyLossFraction is > 0 and <= 1
            && candidate.Parameters.AtrMultiplier is >= 1 and <= 10
            && !string.IsNullOrWhiteSpace(candidate.Reason)
            && candidate.Reason.Length <= 800;

        if (valid)
        {
            return report;
        }

        return report with
        {
            PolicyCandidate = null,
            Output =
                report.Output
                + "\n\nPolicy candidate was rejected: account, evidence, expiry or parameter validation failed.",
        };
    }

    private async Task<string?> ReadNews(CancellationToken cancellationToken)
    {
        if (this.news is null)
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            return await this.news.Read(null, timeout.Token);
        }
        catch (Exception error)
            when (!cancellationToken.IsCancellationRequested
                && error is HttpRequestException or InvalidDataException or JsonException or OperationCanceledException
            )
        {
            return null;
        }
    }

    private async Task RecordAudit(Guid id, TeamRole role, ResearchUsage? usage, ResearchModelAudit? modelAudit)
    {
        if (usage is null && modelAudit is null)
        {
            return;
        }

        await this.gate.WaitAsync(CancellationToken.None);
        try
        {
            if (this.activeRun is not { } current || current.Id != id)
            {
                return;
            }

            var run = current with
            {
                Steps = current
                    .Steps.Select(step =>
                        step.Role == role
                            ? step with
                            {
                                Usage = usage ?? step.Usage,
                                ModelAudit = step.ModelAudit ?? modelAudit,
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

    private async Task VerifyCurrentEvidence(TeamRun run, string evidence, CancellationToken cancellationToken)
    {
        var task = run.Task ?? throw new InvalidOperationException("Research task is missing.");
        using var fresh = JsonDocument.Parse(await this.portfolio.Read(cancellationToken));
        using var original = JsonDocument.Parse(evidence);
        if (
            this.time.GetUtcNow() >= task.ExpiresAt
            || fresh.RootElement.GetProperty("accountKey").GetString() != run.AccountKey
            || ResearchEvidence.PolicyHash(fresh.RootElement) != task.PolicyHash
            || ResearchEvidence.HoldingsDigest(fresh.RootElement)
                != ResearchEvidence.HoldingsDigest(original.RootElement)
            || ResearchEvidence.Mode(fresh.RootElement) != ResearchEvidence.Mode(original.RootElement)
        )
        {
            throw new CodexWorkerException("worker_evidence_changed");
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
                UpdatedAt = this.time.GetUtcNow(),
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
