namespace Portfolio.Checks;

public sealed class TradingReportCompletionTime : TimeProvider
{
    private TimerCallback? callback;

    private object? callbackState;

    public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 5, 0, 0, TimeSpan.Zero);

    public TimeSpan? DeadlineDue { get; private set; }

    public int TimersCreated { get; private set; }

    public override DateTimeOffset GetUtcNow() => this.Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        this.callback = callback;
        this.callbackState = state;
        this.DeadlineDue = dueTime;
        this.TimersCreated++;
        return new Timer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void FireDeadline()
    {
        var callback = this.callback ?? throw new InvalidOperationException("Report completion timer is missing.");
        callback(this.callbackState);
    }
}
