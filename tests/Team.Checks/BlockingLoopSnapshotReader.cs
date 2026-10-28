using Quantara.Team.Application;

namespace Team.Checks;

public sealed class BlockingLoopSnapshotReader : IPortfolioSnapshotReader
{
    private readonly string stage;

    public BlockingLoopSnapshotReader(string stage)
    {
        this.stage = stage;
    }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Entries { get; private set; }

    public Task Refresh(CancellationToken cancellationToken) => this.Wait("refresh", cancellationToken);

    public Task EnsureReady(CancellationToken cancellationToken) => this.Wait("readiness", cancellationToken);

    public async Task<string> Read(CancellationToken cancellationToken)
    {
        await this.Wait("preparation", cancellationToken);
        return await new FakePortfolioSnapshotReader().Read(cancellationToken);
    }

    private async Task Wait(string currentStage, CancellationToken cancellationToken)
    {
        if (currentStage != this.stage)
        {
            return;
        }

        this.Entries += 1;
        this.Entered.TrySetResult();
        await this.Release.Task.WaitAsync(cancellationToken);
    }
}
