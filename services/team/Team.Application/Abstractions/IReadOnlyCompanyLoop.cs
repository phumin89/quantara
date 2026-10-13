namespace Quantara.Team.Application;

public interface IReadOnlyCompanyLoop
{
    public Task<CompanyLoopOverview> Read(CancellationToken cancellationToken);
}
