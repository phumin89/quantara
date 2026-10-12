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
