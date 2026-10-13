using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class FakeHumanOrderReview(FakeOrderStore store, FakeOrderBroker broker, TimeProvider time)
    : IHumanOrderReview
{
    public async Task<OrderView> Review(Guid orderId, bool approve, CancellationToken cancellationToken) =>
        approve
            ? await new ApproveOrderHandler(store, broker, time).Handle(new(orderId, true), cancellationToken)
            : await new RejectOrderHandler(store, time).Handle(new(orderId), cancellationToken);
}
