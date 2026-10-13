using Mediator;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class MediatorHumanOrderReview(IMediator mediator) : IHumanOrderReview
{
    public async Task<OrderView> Review(Guid orderId, bool approve, CancellationToken cancellationToken) =>
        approve
            ? await mediator.Send(new ApproveOrderCommand(orderId, true), cancellationToken)
            : await mediator.Send(new RejectOrderCommand(orderId), cancellationToken);
}
