using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public sealed class TeamCoordinatorLease : IAsyncDisposable
{
    private readonly TeamCoordinator coordinator;

    private int disposed;

    public TeamCoordinatorLease(TeamCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    public Task<TeamRun> Start(string objective, CancellationToken cancellationToken) =>
        this.coordinator.Start(objective, cancellationToken);

    public Task<TeamRun?> RequestStop(Guid id, CancellationToken cancellationToken) =>
        this.coordinator.RequestStop(id, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        await this.coordinator.StopAsync(CancellationToken.None);
        this.coordinator.Dispose();
    }
}
