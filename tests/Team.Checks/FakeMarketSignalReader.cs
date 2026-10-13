using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakeMarketSignalReader : IMarketSignalReader
{
    public MarketSignalNotice? Notice { get; set; }

    public int Reads { get; private set; }

    public Exception? Failure { get; set; }

    public Task<MarketSignalNotice?> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Reads += 1;
        if (this.Failure is not null)
        {
            throw this.Failure;
        }

        return Task.FromResult(this.Notice);
    }
}
