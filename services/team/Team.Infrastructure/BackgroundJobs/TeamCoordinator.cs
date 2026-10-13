using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class TeamCoordinator : BackgroundService, ITeamCoordinator
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Channel<Guid> queue = Channel.CreateBounded<Guid>(1);

    private readonly ITeamRunStore store;

    private readonly ICodexWorker worker;

    private readonly IPortfolioSnapshotReader portfolio;

    private readonly SemaphoreSlim gate = new(1, 1);

    private TeamRun? activeRun;

    private CancellationTokenSource? activeCancellation;

    private bool ready;

    private readonly TaskCompletionSource initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TeamCoordinator(ITeamRunStore store, ICodexWorker worker, IPortfolioSnapshotReader portfolio)
    {
        this.store = store;
        this.worker = worker;
        this.portfolio = portfolio;
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

            var now = DateTimeOffset.UtcNow;
            var run = new TeamRun(
                Guid.NewGuid(),
                objective,
                TeamRunStatus.Queued,
                now,
                now,
                Enum.GetValues<TeamRole>()
                    .Select(role => new TeamStep(role, TeamRunStatus.Queued, null, null))
                    .ToArray(),
                null
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
        var current =
            await this.store.Find(id, cancellationToken) ?? throw new InvalidOperationException("Run missing.");
        const string marker = "Previous review run ID: ";
        var reference = current.Objective.LastIndexOf(marker, StringComparison.Ordinal);
        TeamRun? prior = null;
        if (
            reference >= 0
            && Guid.TryParseExact(current.Objective[(reference + marker.Length)..], "D", out var priorId)
        )
        {
            prior = await this.store.Find(priorId, cancellationToken);
            if (prior is not null && prior.CreatedAt >= current.CreatedAt)
            {
                prior = null;
            }
        }

        snapshot = JsonSerializer.Serialize(
            new
            {
                evidence = JsonSerializer.Deserialize<JsonElement>(snapshot),
                priorReview = prior?.Steps.FirstOrDefault(step => step.Role == TeamRole.Reviewer)?.Output,
                priorRunId = prior?.Id,
                priorRunStatus = prior?.Status.ToString(),
                priorUpdatedAt = prior?.UpdatedAt,
            },
            TeamCoordinator.jsonOptions
        );
        if (snapshot.Length > 60000)
        {
            throw new InvalidOperationException("Research context is too large.");
        }

        var previous = new List<(string Role, string Output)>();
        foreach (var role in Enum.GetValues<TeamRole>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await this.UpdateStep(id, role, TeamRunStatus.Running, null, cancellationToken);
            var output = await this.worker.Run(
                role.ToString().ToLowerInvariant(),
                run.Objective,
                previous,
                snapshot,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            await this.UpdateStep(id, role, TeamRunStatus.Completed, output, cancellationToken);
            previous.Add((role.ToString().ToLowerInvariant(), output));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await this.Finish(id, TeamRunStatus.Completed, null);
    }

    private async Task<TeamRun> UpdateStep(
        Guid id,
        TeamRole role,
        TeamRunStatus status,
        string? output,
        CancellationToken cancellationToken
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
