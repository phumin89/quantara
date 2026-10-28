using Quantara.Team.Application;

namespace Team.Checks;

public sealed class AdvancingMarketSignalReader : IMarketSignalReader
{
    private readonly ManualTimeProvider time;

    private readonly TimeSpan delay;

    private readonly Func<MarketSignalNotice?> notice;

    public AdvancingMarketSignalReader(ManualTimeProvider time, TimeSpan delay, Func<MarketSignalNotice?> notice)
    {
        this.time = time;
        this.delay = delay;
        this.notice = notice;
    }

    public int Reads { get; private set; }

    public Task<MarketSignalNotice?> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Reads += 1;
        this.time.Advance(this.delay);

        return Task.FromResult(this.notice());
    }
}
