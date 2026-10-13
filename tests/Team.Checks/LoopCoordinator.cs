using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Team.Checks;

public sealed class LoopCoordinator : ITeamCoordinator
{
    private readonly FakeTeamRunStore store;

    private readonly TimeProvider time;

    public LoopCoordinator(FakeTeamRunStore store, TimeProvider time)
    {
        this.store = store;
        this.time = time;
    }

    public bool IsConfigured { get; set; } = true;

    public bool FailStart { get; set; }

    public List<string> Objectives { get; } = [];

    public async Task<TeamRun> Start(string objective, CancellationToken cancellationToken)
    {
        this.Objectives.Add(objective);
        if (this.FailStart)
        {
            throw new InvalidOperationException("Uncertain start fixture");
        }

        var now = this.time.GetUtcNow();
        var run = new TeamRun(
            Guid.NewGuid(),
            objective,
            TeamRunStatus.Running,
            now,
            now,
            Enum.GetValues<TeamRole>().Select(role => new TeamStep(role, TeamRunStatus.Running, null, null)).ToArray(),
            null
        );
        await this.store.Save(run, cancellationToken);

        return run;
    }

    public async Task RequestStopCurrent(CancellationToken cancellationToken)
    {
        var run = await this.store.Latest(cancellationToken);
        if (run is not null)
        {
            await this.RequestStop(run.Id, cancellationToken);
        }
    }

    public async Task<TeamRun?> RequestStop(Guid id, CancellationToken cancellationToken)
    {
        var run = await this.store.Find(id, cancellationToken);
        if (run is not null)
        {
            run = run with { Status = TeamRunStatus.Cancelled };
            await this.store.Save(run, cancellationToken);
        }

        return run;
    }
}
