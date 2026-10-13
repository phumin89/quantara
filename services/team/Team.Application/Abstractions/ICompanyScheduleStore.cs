using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public interface ICompanyScheduleStore
{
    public Task<CompanySchedule> Read(CancellationToken cancellationToken);

    public Task Save(CompanySchedule schedule, CancellationToken cancellationToken);
}
