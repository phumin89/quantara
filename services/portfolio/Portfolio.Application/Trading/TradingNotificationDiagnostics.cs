namespace Quantara.Portfolio.Application;

public sealed class TradingNotificationDiagnostics(bool configured)
{
    private int blocked;

    public bool Configured { get; } = configured;

    public bool DashboardAvailable { get; init; }

    public int Blocked => Volatile.Read(ref this.blocked);

    public void SetBlocked(int count) => Interlocked.Exchange(ref this.blocked, Math.Max(0, count));
}
