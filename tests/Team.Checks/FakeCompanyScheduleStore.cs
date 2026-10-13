using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Team.Checks;

public sealed class FakeCompanyScheduleStore : ICompanyScheduleStore
{
    public FakeCompanyScheduleStore(DateTimeOffset now)
    {
        this.Schedule = new CompanySchedule(true, now, null);
    }

    public CompanySchedule Schedule { get; private set; }

    public bool FailSave { get; set; }

    public Task<CompanySchedule> Read(CancellationToken cancellationToken) => Task.FromResult(this.Schedule);

    public Task Save(CompanySchedule schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.FailSave)
        {
            throw new InvalidOperationException("Storage failure fixture");
        }

        this.Schedule = schedule;

        return Task.CompletedTask;
    }
}
