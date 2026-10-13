using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public sealed class FakeTelegramTransport : ITelegramTransport
{
    public string? Token { get; private set; }

    public int Sends { get; private set; }

    public long LastEditedMessage { get; private set; }

    public string? LastStatus { get; private set; }

    public bool FailSend { get; set; }

    public bool FailEdit { get; set; }

    public Task<long> Send(OrderProposal proposal, string token, CancellationToken cancellationToken)
    {
        this.Sends++;
        this.Token = token;
        if (this.FailSend)
        {
            throw new HttpRequestException("Uncertain delivery fixture");
        }

        return Task.FromResult(42L);
    }

    public Task<IReadOnlyList<TelegramClick>> Poll(long offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TelegramClick>>([]);

    public Task Answer(string callbackId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit(long messageId, OrderProposal proposal, string status, CancellationToken cancellationToken)
    {
        if (this.FailEdit)
        {
            throw new HttpRequestException("Edit unavailable fixture");
        }

        this.LastEditedMessage = messageId;
        this.LastStatus = status;
        return Task.CompletedTask;
    }
}
