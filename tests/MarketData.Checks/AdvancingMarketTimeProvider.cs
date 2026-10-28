namespace Quantara.MarketData.Checks;

public sealed class AdvancingMarketTimeProvider(DateTimeOffset value) : TimeProvider
{
    private DateTimeOffset current = value;

    public void Advance(TimeSpan duration)
    {
        this.current += duration;
    }

    public override DateTimeOffset GetUtcNow() => this.current;
}
