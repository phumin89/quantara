using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IOrderStore : IReadOnlyOrderStore
{
    public Task Create(OrderProposal proposal, CancellationToken cancellationToken);

    public Task<bool> Claim(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    public Task Finish(
        Guid id,
        OrderState state,
        string? brokerOrderId,
        string? message,
        CancellationToken cancellationToken
    );

    public Task Reject(Guid id, CancellationToken cancellationToken);

    public Task UpdateObserved(
        Guid id,
        OrderState state,
        string? brokerOrderId,
        string? message,
        CancellationToken cancellationToken
    );
}
