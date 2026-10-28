using System.Data.SqlTypes;
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
            return Task.FromResult(
                this.runs.Values.OrderByDescending(run => run.CreatedAt)
                    .ThenByDescending(run => new SqlGuid(run.Id))
                    .FirstOrDefault()
            );
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
                    .ThenByDescending(run => new SqlGuid(run.Id))
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
                            && !string.IsNullOrEmpty(step.Output?.Trim(' '))
                        )
                    )
                    .OrderByDescending(run => run.CreatedAt)
                    .ThenByDescending(run => new SqlGuid(run.Id))
                    .FirstOrDefault()
            );
        }
    }

    public Task<IReadOnlyList<TeamRun>> LearningHistory(
        string accountKey,
        DateTimeOffset before,
        CancellationToken cancellationToken
    )
    {
        ValidateAccountKey(accountKey);
        var bangkokDate = before.ToOffset(TimeSpan.FromHours(7)).Date;
        var start = new DateTimeOffset(bangkokDate.AddDays(-6), TimeSpan.FromHours(7));
        lock (this.gate)
        {
            return Task.FromResult<IReadOnlyList<TeamRun>>(
                this.runs.Values.Where(run =>
                        run.AccountKey == accountKey
                        && run.CreatedAt >= start
                        && run.CreatedAt < before
                        && run.Status == TeamRunStatus.Completed
                        && run.Learning is not null
                    )
                    .OrderByDescending(run => run.CreatedAt)
                    .ThenByDescending(run => new SqlGuid(run.Id))
                    .ToArray()
            );
        }
    }

    public Task<IReadOnlyList<TeamRun>> RecentResearch(
        string accountKey,
        DateTimeOffset before,
        CancellationToken cancellationToken
    )
    {
        ValidateAccountKey(accountKey);
        lock (this.gate)
        {
            return Task.FromResult<IReadOnlyList<TeamRun>>(
                this.runs.Values.Where(run =>
                        run.AccountKey == accountKey
                        && run.CreatedAt <= before
                        && run.CreatedAt >= before.AddDays(-7).AddMinutes(-1)
                    )
                    .OrderByDescending(run => run.CreatedAt)
                    .ThenByDescending(run => new SqlGuid(run.Id))
                    .Take(512)
                    .ToArray()
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
                    .ThenBy(run => new SqlGuid(run.Id))
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

    private static void ValidateAccountKey(string accountKey)
    {
        if (accountKey is null || accountKey.Length != 64 || accountKey.Any(character => !IsHexDigit(character)))
        {
            throw new ArgumentException("Account key must be exactly 64 hexadecimal characters.", nameof(accountKey));
        }
    }

    private static bool IsHexDigit(char character)
    {
        return character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}
