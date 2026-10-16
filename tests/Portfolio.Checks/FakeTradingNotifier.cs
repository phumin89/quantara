using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class FakeTradingNotifier : ITradingNotifier
{
    public bool IsConfigured { get; set; } = true;

    public bool FailSend { get; set; }

    public bool FailEdit { get; set; }

    public int Sends { get; private set; }

    public int Edits { get; private set; }

    public long LastMessageId { get; private set; }

    public string? LastText { get; private set; }

    public Action? BeforeSend { get; set; }

    public Action? BeforeEdit { get; set; }

    public Task<long> Send(string text, CancellationToken cancellationToken)
    {
        this.BeforeSend?.Invoke();
        this.Sends++;
        this.LastText = text;
        if (this.FailSend)
        {
            throw new HttpRequestException("Uncertain notification fixture.");
        }

        return Task.FromResult(42L);
    }

    public Task Edit(long messageId, string text, CancellationToken cancellationToken)
    {
        this.BeforeEdit?.Invoke();
        this.Edits++;
        this.LastMessageId = messageId;
        this.LastText = text;
        if (this.FailEdit)
        {
            throw new HttpRequestException("Uncertain edit fixture.");
        }

        return Task.CompletedTask;
    }
}
