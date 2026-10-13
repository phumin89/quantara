using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class GetOrdersHandler(IReadOnlyOrderStore store, TimeProvider time)
    : IQueryHandler<GetOrdersQuery, IReadOnlyList<OrderView>>
{
    public async ValueTask<IReadOnlyList<OrderView>> Handle(GetOrdersQuery query, CancellationToken cancellationToken)
    {
        var proposals = await store.List(cancellationToken);
        return proposals.Select(proposal => OrderView.From(proposal, time.GetUtcNow())).ToArray();
    }
}
