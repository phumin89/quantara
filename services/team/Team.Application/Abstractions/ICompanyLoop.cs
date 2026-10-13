namespace Quantara.Team.Application;

public interface ICompanyLoop : IReadOnlyCompanyLoop
{
    public Task<CompanyLoopOverview> SetEnabled(bool enabled, CancellationToken cancellationToken);
}
