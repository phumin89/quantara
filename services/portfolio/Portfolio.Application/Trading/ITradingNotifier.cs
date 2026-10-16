namespace Quantara.Portfolio.Application;

public interface ITradingNotifier
{
    public bool IsConfigured { get; }

    public Task<long> Send(string text, CancellationToken cancellationToken);

    public Task Edit(long messageId, string text, CancellationToken cancellationToken);
}
