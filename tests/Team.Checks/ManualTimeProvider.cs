namespace Team.Checks;

public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => this.now;

    public void Advance(TimeSpan elapsed)
    {
        this.now += elapsed;
    }
}
