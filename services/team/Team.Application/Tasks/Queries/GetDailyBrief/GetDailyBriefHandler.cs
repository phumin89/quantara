using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed class GetDailyBriefHandler : IQueryHandler<GetDailyBriefQuery, IReadOnlyList<TeamRun>>
{
    private readonly IReadOnlyTeamRunStore store;

    public GetDailyBriefHandler(IReadOnlyTeamRunStore store)
    {
        this.store = store;
    }

    public async ValueTask<IReadOnlyList<TeamRun>> Handle(GetDailyBriefQuery query, CancellationToken cancellationToken)
    {
        return await this.store.ForDay(query.Date, cancellationToken);
    }
}
