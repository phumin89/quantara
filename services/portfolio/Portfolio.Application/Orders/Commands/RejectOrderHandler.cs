using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class RejectOrderHandler(IOrderStore store, TimeProvider time)
    : ICommandHandler<RejectOrderCommand, OrderView>
{
    public async ValueTask<OrderView> Handle(RejectOrderCommand command, CancellationToken cancellationToken)
    {
        await store.Reject(command.Id, cancellationToken);
        var proposal =
            await store.Find(command.Id, cancellationToken)
            ?? throw new OrderValidationException("Order proposal not found.");
        return OrderView.From(proposal, time.GetUtcNow());
    }
}
