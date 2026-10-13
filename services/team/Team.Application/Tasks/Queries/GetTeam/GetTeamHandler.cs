using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed class GetTeamHandler : IQueryHandler<GetTeamQuery, TeamRun?>
{
    private readonly IReadOnlyTeamRunStore store;

    private readonly ICurrentAccountReader? account;

    public GetTeamHandler(IReadOnlyTeamRunStore store, ICurrentAccountReader? account = null)
    {
        this.store = store;
        this.account = account;
    }

    public async ValueTask<TeamRun?> Handle(GetTeamQuery query, CancellationToken cancellationToken)
    {
        return this.account is null
            ? await this.store.Latest(cancellationToken)
            : await this.store.LatestForAccount(await this.account.Read(cancellationToken), cancellationToken);
    }
}
