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

    private readonly IOrderProposalPublisher? proposals;

    private readonly SemaphoreSlim gate = new(1, 1);

    private TeamRun? activeRun;

    private CancellationTokenSource? activeCancellation;

    private bool ready;

    private readonly TaskCompletionSource initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TeamCoordinator(
        ITeamRunStore store,
        ICodexWorker worker,
        IPortfolioSnapshotReader portfolio,
        IOrderProposalPublisher? proposals = null
    )
    {
        this.store = store;
        this.worker = worker;
        this.portfolio = portfolio;
        this.proposals = proposals;
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
        using var snapshotDocument = JsonDocument.Parse(snapshot);
        var accountKey = snapshotDocument.RootElement.TryGetProperty("accountKey", out var account)
            ? account.GetString()
            : null;
        if (accountKey is null || accountKey.Length != 64 || accountKey.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("Portfolio snapshot account key is invalid.");
        }

        var current =
            await this.store.Find(id, cancellationToken) ?? throw new InvalidOperationException("Run missing.");
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

        var priorOutput = prior?.Steps.FirstOrDefault(step => step.Role == TeamRole.Reviewer)?.Output;
        var learning = DailyLearning.Review(
            accountKey,
            current.CreatedAt,
            current.Learning,
            evaluatedHistory,
            Math.Clamp(59000 - snapshot.Length - (priorOutput?.Length ?? 0), 3000, 12000)
        );

        snapshot = JsonSerializer.Serialize(
            new
            {
                evidence = JsonSerializer.Deserialize<JsonElement>(snapshot),
                dailyLearning = JsonSerializer.Deserialize<JsonElement>(learning),
                mandate = new
                {
                    dailyNetProfitTargetThb = TradingMandate.DailyNetProfitTargetThb,
                    calendarTimeZone = TradingMandate.CalendarTimeZone,
                    acceptFullCapitalLoss = TradingMandate.AcceptFullCapitalLoss,
                },
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
            var report = await this.worker.Report(
                role.ToString().ToLowerInvariant(),
                run.Objective,
                previous,
                snapshot,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            await this.UpdateStep(id, role, TeamRunStatus.Completed, report.Output, cancellationToken, report);
            previous.Add((role.ToString().ToLowerInvariant(), report.Output));
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
