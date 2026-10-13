using Quantara.Team.Application;

namespace Team.Checks;

public sealed class LoopSnapshotReader : IPortfolioSnapshotReader
{
    public bool Available { get; set; } = true;

    public int Refreshes { get; private set; }

    public Task Refresh(CancellationToken cancellationToken)
    {
        this.Refreshes += 1;

        return Task.CompletedTask;
    }

    public Task<string> Read(CancellationToken cancellationToken)
    {
        if (!this.Available)
        {
            throw new InvalidOperationException("Unavailable fixture");
        }

        return Task.FromResult("{\"positions\":[]}");
    }
}
