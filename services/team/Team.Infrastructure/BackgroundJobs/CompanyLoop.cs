using Microsoft.Extensions.Hosting;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class CompanyLoop : BackgroundService, ICompanyLoop
{
    private const string mission =
        "Operate Quantara's read-only research team without waiting for an owner instruction. "
        + "As CEO, inspect the verified portfolio and choose the most useful analysis priorities. "
        + "Delegate concrete evidence-based work to Analyst; Reviewer must check evidence, risks and uncertainties and propose next research actions. "
        + "Aim to identify opportunities for net profit after costs, but never invent performance or authorize/execute trades. ";

    private readonly ICompanyScheduleStore store;

    private readonly IReadOnlyTeamRunStore runs;

    private readonly ITeamCoordinator coordinator;

    private readonly IPortfolioSnapshotReader portfolio;

    private readonly TimeProvider time;

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
        TimeProvider time
    )
    {
        this.store = store;
        this.runs = runs;
        this.coordinator = coordinator;
        this.portfolio = portfolio;
        this.time = time;
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
                next = next with { NextRunAt = this.time.GetUtcNow() };
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
            if (CompanyLoop.IsActive(latest))
            {
                this.status = CompanyLoopStatus.Running;
                return;
            }

            if (this.schedule.NextRunAt > this.time.GetUtcNow())
            {
                this.status = CompanyLoopStatus.Waiting;
                return;
            }

            try
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probe.CancelAfter(TimeSpan.FromSeconds(5));
                if (this.refreshedAt is null || this.time.GetUtcNow() - this.refreshedAt >= TimeSpan.FromMinutes(5))
                {
                    await this.portfolio.Refresh(probe.Token);
                    this.refreshedAt = this.time.GetUtcNow();
                }

                await this.portfolio.Read(probe.Token);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                this.status = CompanyLoopStatus.WaitingForPortfolio;
                this.error = "Waiting for verified portfolio data before the next analysis.";
                return;
            }

            this.schedule = this.schedule with { NextRunAt = this.time.GetUtcNow().AddHours(1) };
            await this.store.Save(this.schedule, cancellationToken);
            this.refreshedAt = null;
            var objective =
                CompanyLoop.mission
                + $"Assessment time: {this.time.GetUtcNow():O}. Check all supplied timestamps for freshness. ";
            if (latest is not null)
            {
                objective += $"Previous review run ID: {latest.Id:D}";
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
