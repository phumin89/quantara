using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IReadOnlyOrderStore
{
    public Task<IReadOnlyList<OrderProposal>> List(CancellationToken cancellationToken);

    public Task<OrderProposal?> Find(Guid id, CancellationToken cancellationToken);
}
