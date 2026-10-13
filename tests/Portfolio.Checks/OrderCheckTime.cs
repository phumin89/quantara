namespace Portfolio.Checks;

public sealed class OrderCheckTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 5, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => this.Now;
}
