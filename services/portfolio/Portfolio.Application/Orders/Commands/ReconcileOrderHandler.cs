using Mediator;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed class ReconcileOrderHandler(IOrderStore store, IOrderBroker broker, TimeProvider time)
    : ICommandHandler<ReconcileOrderCommand, OrderView>
{
    public async ValueTask<OrderView> Handle(ReconcileOrderCommand command, CancellationToken cancellationToken)
    {
        var proposal =
            await store.Find(command.Id, cancellationToken)
            ?? throw new OrderValidationException("Order proposal not found.");
        if (
            proposal.State
            is not (OrderState.Unknown or OrderState.Submitting or OrderState.Submitted or OrderState.PartiallyFilled)
        )
        {
            return OrderView.From(proposal, time.GetUtcNow());
        }

        if (proposal.State == OrderState.Submitting && time.GetUtcNow() - proposal.CreatedAt < TimeSpan.FromMinutes(4))
        {
            throw new OrderValidationException("Submission is still in progress. Wait before checking its result.");
        }

        var observed = await broker.Inspect(proposal, cancellationToken);
        if (
            observed.State
            is not (
                OrderState.Unknown
                or OrderState.Submitted
                or OrderState.PartiallyFilled
                or OrderState.Filled
                or OrderState.Cancelled
            )
        )
        {
            throw new OrderValidationException("Broker returned an unsupported outcome.");
        }

        await store.UpdateObserved(
            proposal.Id,
            observed.State,
            observed.BrokerOrderId,
            observed.Message,
            cancellationToken
        );
        var current =
            await store.Find(proposal.Id, cancellationToken)
            ?? throw new OrderValidationException("Order proposal not found.");
        return OrderView.From(current, time.GetUtcNow());
    }
}
