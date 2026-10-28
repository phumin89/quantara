using Quantara.Team.Application;

namespace Team.Checks;

public sealed class AdvancingLoopSnapshotReader : IPortfolioSnapshotReader
{
    private readonly ManualTimeProvider time;

    private readonly TimeSpan delay;

    public AdvancingLoopSnapshotReader(ManualTimeProvider time, TimeSpan delay)
    {
        this.time = time;
        this.delay = delay;
    }

    public int Reads { get; private set; }

    public Task Refresh(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }

    public Task<string> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Reads += 1;
        this.time.Advance(this.delay);

        return Task.FromResult("{\"positions\":[]}");
    }
}
