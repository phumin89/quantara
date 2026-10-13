using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed class GetDailyBriefHandler : IQueryHandler<GetDailyBriefQuery, IReadOnlyList<TeamRun>>
{
    private readonly IReadOnlyTeamRunStore store;

    private readonly ICurrentAccountReader? account;

    public GetDailyBriefHandler(IReadOnlyTeamRunStore store, ICurrentAccountReader? account = null)
    {
        this.store = store;
        this.account = account;
    }

    public async ValueTask<IReadOnlyList<TeamRun>> Handle(GetDailyBriefQuery query, CancellationToken cancellationToken)
    {
        var runs = await this.store.ForDay(query.Date, cancellationToken);
        if (this.account is null)
        {
            return runs;
        }

        var accountKey = await this.account.Read(cancellationToken);
        return runs.Where(run => run.AccountKey == accountKey).ToArray();
    }
}
