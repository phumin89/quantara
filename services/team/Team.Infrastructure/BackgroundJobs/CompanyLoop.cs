using Microsoft.Extensions.Hosting;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class CompanyLoop : BackgroundService, ICompanyLoop
{
    private const string mission =
        "Operate Quantara as a high-risk daily spot trading company without waiting for research instructions. "
        + "Owner target: 500 THB realized net profit per Bangkok calendar day after actual trading costs. This is a goal, never a guaranteed return or a forced trade quota. "
        + "The owner accepts losing all allocated capital. No mandatory cash reserve: a cash-only portfolio requires actively seeking entry opportunities across the full supplied THB spot scan. "
        + "CEO must rank feasible setups; Analyst evaluates entry, size, exit, invalidation and costs; Reviewer concludes Buy, Sell or Wait and may return one supported spot LIMIT proposal for explicit human approval. "
        + "If Wait, explain the best rejected alternatives, opportunity-cost uncertainty and the next reconsideration condition. A missing breakout does not eliminate other setups. Never invent performance, count hypothetical/unrealized gains toward the target, approve or execute trades. "
        + "Operate around the clock, including overnight, weekends and Bangkok midnight. Prioritize fresh current spot setups and reassessment of actual-held positions and exits before unrelated simulations or protocol drafts. Midnight and day-target accounting never close positions, reset funds, stop watching, or force trades; the target is not a ceiling. Keep unfinished research tasks in the ledger, but they must not consume live opportunity work or block an independent supported proposal. ";

    private readonly ICompanyScheduleStore store;

    private readonly IReadOnlyTeamRunStore runs;

    private readonly ITeamCoordinator coordinator;

    private readonly IPortfolioSnapshotReader portfolio;

    private readonly TimeProvider time;

    private readonly IMarketSignalReader? signals;

    private DateTimeOffset? lastSignalPollAt;

    private MarketSignalNotice? signal;

    private readonly SemaphoreSlim gate = new(1, 1);

    private CompanySchedule? schedule;

    private CompanyLoopStatus status = CompanyLoopStatus.Waiting;

    private string? error;

    private DateTimeOffset? refreshedAt;

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
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            this.schedule ??= await this.store.Read(cancellationToken);
            var next = this.schedule with { Enabled = enabled };
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
        await this.gate.WaitAsync(cancellationToken);
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
                && latest.Learning?.EvaluationAt is DateTimeOffset deadline
                && this.schedule.NextRunAt >= latest.CreatedAt.AddMinutes(59)
                && this.schedule.NextRunAt < deadline
                && deadline <= this.schedule.NextRunAt.AddMinutes(15)
            )
            {
                this.schedule = this.schedule with { NextRunAt = deadline };
                await this.store.Save(this.schedule, cancellationToken);
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

            var hourly = this.schedule.NextRunAt <= now;
            var triggered =
                this.signal is not null
                && this.signal.Key != this.schedule.LastSignalKey
                && this.signal.CheckedAt <= now.AddSeconds(5)
                && now - this.signal.CheckedAt <= TimeSpan.FromMinutes(2)
                && (
                    this.schedule.LastAnalysisAt is null
                    || now - this.schedule.LastAnalysisAt >= TimeSpan.FromMinutes(15)
                );
            if (!hourly && !triggered)
            {
                this.status = CompanyLoopStatus.Waiting;
                return;
            }

            if (
                this.schedule.LastAnalysisAt is not null
                && now - this.schedule.LastAnalysisAt < TimeSpan.FromMinutes(15)
            )
            {
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
            objective += $"Trading day (Bangkok): {this.time.GetUtcNow().ToOffset(TimeSpan.FromHours(7)):yyyy-MM-dd}. ";
            if (latest is not null)
            {
                objective += $"Previous review run ID: {latest.Id:D}";
            }

            if (triggered)
            {
                objective +=
                    " Market alert (unvalidated research heuristic, not an order): " + this.signal?.Description;
            }

            try
            {
                var run = await this.coordinator.Start(objective, cancellationToken);
                this.schedule = this.schedule with { LastRunId = run.Id };
                await this.store.Save(this.schedule, cancellationToken);
                this.status = CompanyLoopStatus.Running;
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
        finally
        {
            this.gate.Release();
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

    private CompanyLoopOverview Overview(CompanyLoopStatus visibleStatus)
    {
        var current = this.schedule ?? throw new InvalidOperationException("Company schedule is unavailable.");

        return new CompanyLoopOverview(
            current.Enabled,
            visibleStatus,
            current.Enabled ? current.NextRunAt : null,
            current.LastRunId,
            this.error
        );
    }
}
