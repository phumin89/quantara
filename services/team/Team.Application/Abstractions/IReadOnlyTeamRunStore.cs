using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public interface IReadOnlyTeamRunStore
{
    public Task<TeamRun?> Latest(CancellationToken cancellationToken);

    public Task<TeamRun?> Find(Guid id, CancellationToken cancellationToken);

    public Task<IReadOnlyList<TeamRun>> ForDay(DateOnly day, CancellationToken cancellationToken);
}
