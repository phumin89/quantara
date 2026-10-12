using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed class GetTeamHandler : IQueryHandler<GetTeamQuery, TeamRun?>
{
    private readonly IReadOnlyTeamRunStore store;

    public GetTeamHandler(IReadOnlyTeamRunStore store)
    {
        this.store = store;
    }

    public async ValueTask<TeamRun?> Handle(GetTeamQuery query, CancellationToken cancellationToken)
    {
        return await this.store.Latest(cancellationToken);
    }
}
