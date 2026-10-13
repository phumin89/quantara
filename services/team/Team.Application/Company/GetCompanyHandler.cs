using Mediator;

namespace Quantara.Team.Application;

public sealed class GetCompanyHandler : IQueryHandler<GetCompanyQuery, CompanyLoopOverview>
{
    private readonly IReadOnlyCompanyLoop loop;

    public GetCompanyHandler(IReadOnlyCompanyLoop loop)
    {
        this.loop = loop;
    }

    public async ValueTask<CompanyLoopOverview> Handle(GetCompanyQuery query, CancellationToken cancellationToken)
    {
        return await this.loop.Read(cancellationToken);
    }
}
