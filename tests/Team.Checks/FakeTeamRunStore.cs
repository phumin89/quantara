using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Team.Checks;

public sealed class FakeTeamRunStore : ITeamRunStore
{
    private readonly object gate = new();

    private readonly Dictionary<Guid, TeamRun> runs = new();

    private readonly List<TeamRunWaiter> waiters = [];

    public Task<TeamRun?> Latest(CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            return Task.FromResult(this.runs.Values.OrderByDescending(run => run.CreatedAt).FirstOrDefault());
        }
    }

    public Task<TeamRun?> Find(Guid id, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            return Task.FromResult(this.runs.GetValueOrDefault(id));
        }
    }

    public Task<TeamRun?> LatestForAccount(string accountKey, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            return Task.FromResult(
                this.runs.Values.Where(run => run.AccountKey == accountKey)
                    .OrderByDescending(run => run.CreatedAt)
                    .ThenByDescending(run => run.Id)
                    .FirstOrDefault()
            );
        }
    }

    public Task<TeamRun?> LatestReviewed(string accountKey, DateTimeOffset before, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            return Task.FromResult(
                this.runs.Values.Where(run => run.AccountKey == accountKey && run.CreatedAt < before)
                    .Where(run =>
                        run.Steps.Any(step =>
                            step.Role == TeamRole.Reviewer
                            && step.Status == TeamRunStatus.Completed
                            && !string.IsNullOrEmpty(step.Output)
                        )
                    )
                    .OrderByDescending(run => run.CreatedAt)
                    .ThenByDescending(run => run.Id)
                    .FirstOrDefault()
            );
        }
    }

    public Task<IReadOnlyList<TeamRun>> ForDay(DateOnly day, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        var end = start.AddDays(1);
        lock (this.gate)
        {
            return Task.FromResult<IReadOnlyList<TeamRun>>(
                this.runs.Values.Where(run => run.CreatedAt >= start && run.CreatedAt < end)
                    .OrderBy(run => run.CreatedAt)
                    .ThenBy(run => run.Id)
                    .ToArray()
            );
        }
    }

    public Task Save(TeamRun run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            this.runs[run.Id] = run;
            foreach (var waiter in this.waiters.Where(item => item.Id == run.Id && item.Predicate(run)).ToArray())
            {
                waiter.Completion.TrySetResult(run);
                this.waiters.Remove(waiter);
            }
        }

        return Task.CompletedTask;
    }

    public Task Recover(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<TeamRun> WaitFor(Guid id, Func<TeamRun, bool> predicate, CancellationToken cancellationToken)
    {
        var waiter = new TeamRunWaiter(
            id,
            predicate,
            new TaskCompletionSource<TeamRun>(TaskCreationOptions.RunContinuationsAsynchronously)
        );
        lock (this.gate)
        {
            if (this.runs.TryGetValue(id, out var run) && predicate(run))
            {
                return run;
            }

            this.waiters.Add(waiter);
        }

        try
        {
            return await waiter.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            lock (this.gate)
            {
                this.waiters.Remove(waiter);
            }
        }
    }
}
