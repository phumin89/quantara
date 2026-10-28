namespace Team.Checks;

public sealed class SnapshotCollectionTime : TimeProvider
{
    private TimerCallback? callback;

    private object? callbackState;

    public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);

    public TimeSpan? DeadlineDue { get; private set; }

    public override DateTimeOffset GetUtcNow() => this.Now;

    public void Advance(TimeSpan elapsed) => this.Now += elapsed;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        this.callback = callback;
        this.callbackState = state;
        this.DeadlineDue = dueTime;

        return new Timer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void FireDeadline()
    {
        var callback = this.callback ?? throw new InvalidOperationException("Snapshot deadline timer is missing.");
        callback(this.callbackState);
    }
}
