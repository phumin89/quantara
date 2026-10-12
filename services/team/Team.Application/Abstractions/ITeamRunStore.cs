using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public interface ITeamRunStore : IReadOnlyTeamRunStore
{
    public Task Save(TeamRun run, CancellationToken cancellationToken);

    public Task Recover(CancellationToken cancellationToken);
}
