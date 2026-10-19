using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public interface ITeamCoordinator
{
    public Task<TeamRun> Start(string objective, CancellationToken cancellationToken);

    public Task<TeamRun> StartAutomatic(
        string objective,
        CancellationToken cancellationToken,
        bool marketTriggered = false
    ) => this.Start(objective, cancellationToken);

    public bool IsConfigured { get; }

    public Task<TeamRun?> RequestStop(Guid id, CancellationToken cancellationToken);

    public Task RequestStopCurrent(CancellationToken cancellationToken);
}
