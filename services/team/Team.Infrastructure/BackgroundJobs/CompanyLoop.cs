using Microsoft.Extensions.Hosting;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class CompanyLoop : BackgroundService, ICompanyLoop
{
    private const string mission =
        "Operate Quantara's high-risk THB spot research team around the clock without waiting for instructions. "
        + "Read the supplied account-scoped capital-risk preferences; seek opportunities across the full supplied scan, including when cash-only. "
        + "Use the current account objective supplied with portfolio evidence, not the legacy daily profit field. Separate verified cash, holdings, deposits and trading P&L; unknown costs stay Unknown. "
        + "Prioritize held positions, fresh market setups and relevant verified news. The server assigns one scoped specialist task; Reviewer validates that deliverable and returns an evidence-bound read-only assessment. "
        + "For Wait, name rejected alternatives, exact missing fields, reconsideration conditions and next review. Hypothetical opportunity cost is separate from account profit. "
        + "No model approves or executes orders. ManualReview proposals retain explicit human approval; the deterministic engine owns its unchanged mandate. "
        + "Midnight and business targets never force trades, close positions, reset capital or expand authority. Keep protocol experiments separate from current opportunity research. ";

    private readonly ICompanyScheduleStore store;

    private readonly IReadOnlyTeamRunStore runs;

    private readonly ITeamCoordinator coordinator;

    private readonly IPortfolioSnapshotReader portfolio;

    private readonly TimeProvider time;

    private readonly IMarketSignalReader? signals;

    private DateTimeOffset? lastSignalPollAt;

    private MarketSignalNotice? signal;

    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly object interruptionGate = new();

    private CancellationTokenSource? iterationCancellation;

    private int pendingStops;

    private CompanySchedule? schedule;

    private CompanyLoopStatus status = CompanyLoopStatus.Waiting;

    private string? error;

    private DateTimeOffset? refreshedAt;

    private DateTimeOffset? lastCompletedAt;

    private TeamRole? activeRole;

    public CompanyLoop(
        ICompanyScheduleStore store,
        IReadOnlyTeamRunStore runs,
        ITeamCoordinator coordinator,
        IPortfolioSnapshotReader portfolio,
        TimeProvider time,
        IMarketSignalReader? signals = null
    )
    {
        this.store = store;
        this.runs = runs;
        this.coordinator = coordinator;
        this.portfolio = portfolio;
        this.time = time;
        this.signals = signals;
    }

    public async Task<CompanyLoopOverview> Read(CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            this.schedule ??= await this.store.Read(cancellationToken);
            var latest = await this.runs.Latest(cancellationToken);
            if (latest?.Status == TeamRunStatus.Completed)
            {
                this.lastCompletedAt = latest.UpdatedAt;
            }
            else if (latest?.AccountKey is { } accountKey)
            {
                this.lastCompletedAt = (
                    await this.runs.LatestReviewed(accountKey, this.time.GetUtcNow(), cancellationToken)
                )?.UpdatedAt;
            }

            this.activeRole = latest?.Steps.FirstOrDefault(step => step.Status == TeamRunStatus.Running)?.Role;
            var visibleStatus = this.status;
            if (!this.schedule.Enabled)
            {
                visibleStatus = this.error is null ? CompanyLoopStatus.Stopped : CompanyLoopStatus.Error;
            }
            else if (!this.coordinator.IsConfigured)
            {
                visibleStatus = CompanyLoopStatus.WaitingForCodex;
            }
            else if (CompanyLoop.IsActive(latest))
            {
                visibleStatus = CompanyLoopStatus.Running;
            }
            else if (this.error is not null)
            {
                visibleStatus =
                    this.status == CompanyLoopStatus.WaitingForPortfolio
                        ? CompanyLoopStatus.WaitingForPortfolio
                        : CompanyLoopStatus.Error;
            }
            else if (this.schedule.DeferredReason is not null)
            {
                visibleStatus =
                    this.schedule.DeferredReason == "waiting_for_evidence"
                        ? CompanyLoopStatus.Waiting
                        : CompanyLoopStatus.DeferredBudget;
            }
            else if (latest?.Status == TeamRunStatus.Failed && visibleStatus != CompanyLoopStatus.DeferredBudget)
            {
                return this.Overview(CompanyLoopStatus.Error) with
                {
                    Error = latest.Error ?? "The last analysis failed. The next scheduled assessment remains enabled.",
                };
            }
            else if (visibleStatus == CompanyLoopStatus.Running)
            {
                visibleStatus = CompanyLoopStatus.Waiting;
            }

            return this.Overview(visibleStatus);
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task<CompanyLoopOverview> SetEnabled(bool enabled, CancellationToken cancellationToken)
    {
        if (enabled)
        {
            await this.gate.WaitAsync(cancellationToken);
        }
        else
        {
            Task interruption;
            lock (this.interruptionGate)
            {
                this.pendingStops += 1;
                interruption = this.iterationCancellation?.CancelAsync() ?? Task.CompletedTask;
            }

            try
            {
                await interruption;
                await this.gate.WaitAsync(cancellationToken);
            }
            finally
            {
                lock (this.interruptionGate)
                {
                    this.pendingStops -= 1;
                }
            }
        }

        try
        {
            this.schedule ??= await this.store.Read(cancellationToken);
            var next = this.schedule with { Enabled = enabled, DeferredReason = null, NextRole = null };
            if (enabled && !this.schedule.Enabled)
            {
                next = next with { NextRunAt = this.time.GetUtcNow(), LastAnalysisAt = null };
            }

            // Stop is honored in memory even if storage is temporarily unavailable.
            if (!enabled)
            {
                this.schedule = next;
                this.status = CompanyLoopStatus.Stopped;
                try
                {
                    await this.store.Save(next, CancellationToken.None);
                }
                catch (Exception)
                {
                    this.error =
                        "Company stopped locally, but the saved stop could not be confirmed. Retry Stop before restarting the service.";
                    throw;
                }
                finally
                {
                    await this.coordinator.RequestStopCurrent(CancellationToken.None);
                }
            }
            else
            {
                await this.store.Save(next, cancellationToken);
            }

            this.schedule = next;
            this.status = enabled ? CompanyLoopStatus.Waiting : CompanyLoopStatus.Stopped;
            this.error = null;

            return this.Overview(this.status);
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task Tick(CancellationToken cancellationToken)
    {
        using var iteration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            lock (this.interruptionGate)
            {
                if (this.pendingStops > 0)
                {
                    return;
                }

                this.iterationCancellation = iteration;
            }

            await this.TickCore(iteration.Token);
        }
        catch (OperationCanceledException)
            when (iteration.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The pending Stop command persists the disabled schedule after this gate is released.
        }
        finally
        {
            lock (this.interruptionGate)
            {
                this.iterationCancellation = null;
            }

            this.gate.Release();
        }
    }

    private async Task TickCore(CancellationToken cancellationToken)
    {
        try
        {
            this.schedule ??= await this.store.Read(cancellationToken);
            if (!this.schedule.Enabled)
            {
                this.status = CompanyLoopStatus.Stopped;
                return;
            }

            if (!this.coordinator.IsConfigured)
            {
                this.status = CompanyLoopStatus.WaitingForCodex;
                this.error = null;
                return;
            }

            var latest = await this.runs.Latest(cancellationToken);
            if (
                latest?.Status == TeamRunStatus.Completed
                && this.schedule.LastRunId == latest.Id
                && (this.schedule.LastAnalysisAt is null || this.schedule.LastAnalysisAt <= latest.CreatedAt)
            )
            {
                var nextRunAt = this.NextReviewOrLearningAt(latest, this.schedule);
                if (nextRunAt is DateTimeOffset deadline && deadline != this.schedule.NextRunAt)
                {
                    this.schedule = this.schedule with { NextRunAt = deadline };
                    await this.store.Save(this.schedule, cancellationToken);
                }
            }

            if (CompanyLoop.IsActive(latest))
            {
                this.status = CompanyLoopStatus.Running;
                return;
            }

            var now = this.time.GetUtcNow();
            if (
                this.signals is not null
                && (this.lastSignalPollAt is null || now - this.lastSignalPollAt >= TimeSpan.FromMinutes(1))
            )
            {
                this.lastSignalPollAt = now;
                this.signal = null;
                try
                {
                    using var signalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    signalTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    this.signal = await this.signals.Read(signalTimeout.Token);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // Unavailable signals never block a regular hourly assessment or authorize a trade.
                }
            }

            now = this.time.GetUtcNow();
            var hourly = this.schedule.NextRunAt <= now;
            var triggered = this.HasFreshSignal(now);
            if (!hourly && !triggered)
            {
                if (this.status == CompanyLoopStatus.WaitingForPortfolio)
                {
                    this.error = null;
                }

                if (this.status != CompanyLoopStatus.DeferredBudget)
                {
                    this.status = CompanyLoopStatus.Waiting;
                }

                return;
            }

            if (
                latest?.Task is null
                && this.schedule.LastAnalysisAt is not null
                && now - this.schedule.LastAnalysisAt < TimeSpan.FromMinutes(15)
            )
            {
                var cooldown = this.schedule.LastAnalysisAt.Value.AddMinutes(15);
                if (this.schedule.NextRunAt < cooldown)
                {
                    this.schedule = this.schedule with { NextRunAt = cooldown };
                    await this.store.Save(this.schedule, cancellationToken);
                }

                this.status = CompanyLoopStatus.Waiting;
                return;
            }

            try
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probe.CancelAfter(TimeSpan.FromSeconds(5));
                if (
                    hourly
                    && (this.refreshedAt is null || this.time.GetUtcNow() - this.refreshedAt >= TimeSpan.FromMinutes(5))
                )
                {
                    await this.portfolio.Refresh(probe.Token);
                    this.refreshedAt = this.time.GetUtcNow();
                }

                await this.portfolio.EnsureReady(probe.Token);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                this.status = CompanyLoopStatus.WaitingForPortfolio;
                this.error = "Waiting for verified portfolio data before the next analysis.";
                return;
            }

            now = this.time.GetUtcNow();
            hourly = this.schedule.NextRunAt <= now;
            triggered = this.HasFreshSignal(now);
            if (!hourly && !triggered)
            {
                this.status = CompanyLoopStatus.Waiting;
                this.error = null;
                return;
            }

            this.schedule = this.schedule with
            {
                NextRunAt = hourly ? this.time.GetUtcNow().AddHours(1) : this.schedule.NextRunAt,
                LastSignalKey = triggered ? this.signal?.Key : this.schedule.LastSignalKey,
                LastAnalysisAt = this.time.GetUtcNow(),
            };
            await this.store.Save(this.schedule, cancellationToken);
            this.refreshedAt = null;
            var objective =
                CompanyLoop.mission
                + $"Assessment time: {this.time.GetUtcNow():O}. Check all supplied timestamps for freshness. ";
            objective +=
                "Read the current account goal only from supplied account-checked portfolio evidence. If unavailable, report Target unavailable; never infer a target amount or deadline. ";
            objective += $"Trading day (Bangkok): {this.time.GetUtcNow().ToOffset(TimeSpan.FromHours(7)):yyyy-MM-dd}. ";
            if (latest is not null)
            {
                objective += $"Previous review run ID: {latest.Id:D}";
            }

            if (triggered)
            {
                objective +=
                    " Market alert (unvalidated research heuristic, not an order): inspect supplied market evidence.";
            }

            try
            {
                var run = await this.coordinator.StartAutomatic(objective, cancellationToken, triggered);
                this.schedule = this.schedule with
                {
                    LastRunId = run.Id,
                    NextRunAt = run.Task is not null ? this.time.GetUtcNow().AddMinutes(15) : this.schedule.NextRunAt,
                    DeferredReason = null,
                    NextRole = null,
                };
                await this.store.Save(this.schedule, cancellationToken);
                this.status = CompanyLoopStatus.Running;
                this.error = null;
            }
            catch (ResearchBudgetDeferredException error)
            {
                this.schedule = this.schedule with
                {
                    NextRunAt = error.Decision.NextAllowedAt ?? this.time.GetUtcNow().AddHours(1),
                    DeferredReason = error.Decision.Reason,
                    NextRole = error.Decision.NextRole,
                };
                await this.store.Save(this.schedule, cancellationToken);
                this.status =
                    error.Decision.Reason == "waiting_for_evidence"
                        ? CompanyLoopStatus.Waiting
                        : CompanyLoopStatus.DeferredBudget;
                this.error = null;
            }
            catch (TeamBusyException)
            {
                this.status = CompanyLoopStatus.Running;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            this.status = CompanyLoopStatus.Error;
            this.error = "Company analysis is unavailable. Check the connection and retry.";
        }
    }

    public override void Dispose()
    {
        this.gate.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await this.Tick(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(30), this.time, stoppingToken);
        }
    }

    private static bool IsActive(TeamRun? run)
    {
        return run?.Status is TeamRunStatus.Queued or TeamRunStatus.Running;
    }

    private bool HasFreshSignal(DateTimeOffset now) =>
        this.signal is { } candidate
        && candidate.Key != this.schedule?.LastSignalKey
        && candidate.CheckedAt <= now.AddSeconds(5)
        && now - candidate.CheckedAt <= TimeSpan.FromMinutes(2);

    private DateTimeOffset? NextReviewOrLearningAt(TeamRun latest, CompanySchedule current)
    {
        DateTimeOffset? next = null;
        if (
            latest.Learning?.EvaluationAt is DateTimeOffset deadline
            && current.NextRunAt >= latest.CreatedAt.AddMinutes(59)
            && current.NextRunAt < deadline
            && deadline <= current.NextRunAt.AddMinutes(15)
        )
        {
            next = deadline;
        }

        var assessment = latest.Assessment;
        var now = this.time.GetUtcNow();
        if (
            !string.IsNullOrWhiteSpace(latest.AccountKey)
            && assessment is not null
            && assessment.SchemaVersion == "research-assessment-v1"
            && assessment.RunId == latest.Id
            && assessment.AccountKey == latest.AccountKey
            && assessment.ExpiresAt > now
        )
        {
            var reviews = assessment
                .Positions.Where(position =>
                    position.ReviewAfter > assessment.AsOf
                    && position.ReviewAfter < position.ExpiresAt
                    && position.ExpiresAt <= assessment.ExpiresAt
                    && position.ExpiresAt > now
                )
                .Select(position => position.ReviewAfter)
                .Concat(
                    assessment
                        .Opportunities.Where(opportunity =>
                            opportunity.ReviewAfter > assessment.AsOf && opportunity.ReviewAfter < assessment.ExpiresAt
                        )
                        .Select(opportunity => opportunity.ReviewAfter)
                );
            var review = reviews.DefaultIfEmpty().Min();
            if (review != default)
            {
                var cooldown = (current.LastAnalysisAt ?? DateTimeOffset.MinValue).AddMinutes(15);
                var candidate = review > cooldown ? review : cooldown;
                if (candidate < assessment.ExpiresAt && candidate < (next ?? current.NextRunAt))
                {
                    next = candidate;
                }
            }
        }

        return next;
    }

    private CompanyLoopOverview Overview(CompanyLoopStatus visibleStatus)
    {
        var current = this.schedule ?? throw new InvalidOperationException("Company schedule is unavailable.");

        return new CompanyLoopOverview(
            current.Enabled,
            visibleStatus,
            current.Enabled ? current.NextRunAt : null,
            current.LastRunId,
            this.error,
            LastCompletedAt: this.lastCompletedAt,
            ActiveRole: this.activeRole,
            NextRole: current.NextRole,
            BudgetReason: current.DeferredReason
        );
    }
}
