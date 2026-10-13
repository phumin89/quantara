using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public interface ITelegramTransport
{
    public Task<long> Send(OrderProposal proposal, string token, CancellationToken cancellationToken);

    public Task<IReadOnlyList<TelegramClick>> Poll(long offset, CancellationToken cancellationToken);

    public Task Answer(string callbackId, string text, CancellationToken cancellationToken);

    public Task Edit(long messageId, OrderProposal proposal, string status, CancellationToken cancellationToken);
}
