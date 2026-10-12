using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public interface ITeamCoordinator
{
    public Task<TeamRun> Start(string objective, CancellationToken cancellationToken);

    public bool IsConfigured { get; }

    public Task<TeamRun?> RequestStop(Guid id, CancellationToken cancellationToken);
}
