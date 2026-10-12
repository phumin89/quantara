using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Team.Checks;

public sealed class FakeCoordinator : ITeamCoordinator
{
    public bool IsConfigured => true;

    public Task<TeamRun> Start(string objective, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<TeamRun?> RequestStop(Guid id, CancellationToken cancellationToken) => Task.FromResult<TeamRun?>(null);
}
