using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class HangfireDeliveryNotifier : ITradingNotifier
{
    public bool IsConfigured { get; init; } = true;

    public int Sends { get; private set; }

    public Func<Task<long>>? SendBehavior { get; init; }

    public async Task<long> Send(string text, CancellationToken cancellationToken)
    {
        this.Sends++;
        return this.SendBehavior is null ? 42 : await this.SendBehavior();
    }

    public Task Edit(long messageId, string text, CancellationToken cancellationToken) => Task.CompletedTask;
}
