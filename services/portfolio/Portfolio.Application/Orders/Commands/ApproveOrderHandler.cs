using Mediator;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed class ApproveOrderHandler(
    IOrderStore store,
    IOrderBroker broker,
    TimeProvider time,
    IRiskSizingService? risk = null,
    IReadOnlyTradingStore? trading = null
) : ICommandHandler<ApproveOrderCommand, OrderView>
{
    public async ValueTask<OrderView> Handle(ApproveOrderCommand command, CancellationToken cancellationToken)
    {
        if (!command.Confirmed)
        {
            throw new OrderValidationException("Review this exact order and confirm before submitting.");
        }

        var proposal =
            await store.Find(command.Id, cancellationToken)
            ?? throw new OrderValidationException("Order proposal not found.");
        if (proposal.State != OrderState.Pending)
        {
            return OrderView.From(proposal, time.GetUtcNow());
        }

        if (time.GetUtcNow() >= proposal.ExpiresAt)
        {
            throw new OrderValidationException("Approval expired. Create and review a new proposal.");
        }

        if (
            trading is not null
            && (await trading.Read(cancellationToken)).Mode == Quantara.Portfolio.Domain.Trading.TradingMode.AutoLive
        )
        {
            throw new OrderValidationException(
                "Disable Auto Trading before submitting a separately reviewed manual order."
            );
        }

        var market = await broker.Market(proposal.Symbol, proposal.Side, cancellationToken);
        OrderRules.Validate(proposal, market);
        if (decimal.Abs(market.ReferencePrice / proposal.ReferencePrice - 1) > 0.01m)
        {
            throw new OrderValidationException(
                "Market price changed by more than 1%. Create and review a new proposal. Your limit price was not changed."
            );
        }

        var riskCapital = risk is null ? null : await risk.Revalidate(proposal, cancellationToken);
        if (risk is null && proposal.Sizing is not null)
        {
            throw new OrderValidationException("Adaptive risk validation is unavailable. No order was sent.");
        }

        var balances = await broker.Balances(cancellationToken);
        if (
            riskCapital is not null
            && (
                balances.Any(balance =>
                    balance.Quantity
                        != (riskCapital.Assets.SingleOrDefault(asset => asset.Symbol == balance.Symbol)?.Quantity ?? 0)
                    || balance.Available
                        != (riskCapital.Assets.SingleOrDefault(asset => asset.Symbol == balance.Symbol)?.Available ?? 0)
                )
                || riskCapital.Assets.Any(asset =>
                    asset.Quantity > 0 && !balances.Any(balance => balance.Symbol == asset.Symbol)
                )
            )
        )
        {
            throw new OrderValidationException(
                "Account balances changed after sizing. Request a fresh proposal; the reviewed amount was not changed."
            );
        }

        var currency = proposal.Side == TradeSide.Buy ? "THB" : proposal.Symbol;
        var available = balances
            .FirstOrDefault(balance => balance.Symbol.Equals(currency, StringComparison.OrdinalIgnoreCase))
            ?.Available;
        var required =
            proposal.Side == TradeSide.Buy && proposal.Sizing is not null
                ? proposal.Amount
                    + decimal.Ceiling(proposal.Amount * proposal.Sizing.Policy.EstimatedFeeRate * 100m) / 100m
                : proposal.Amount;
        if (available is null || available < required)
        {
            throw new OrderValidationException("Insufficient available balance for this order.");
        }

        using var prepared = await broker.Prepare(proposal, cancellationToken);

        if (!await store.Claim(proposal.Id, time.GetUtcNow(), cancellationToken))
        {
            throw new OrderValidationException(
                "Order expired, was already handled, or another submission needs reconciliation. Reload and check Bitkub before trying another order."
            );
        }

        // A durable claim precedes the POST. Client cancellation never triggers a second submission.
        var state = OrderState.Unknown;
        string? brokerOrderId = null;
        string? message =
            "Submission outcome is unknown. Check Bitkub before creating another order; this proposal will never be resent.";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await prepared.Send(timeout.Token);
            state = result.Accepted ? OrderState.Submitted : OrderState.Rejected;
            brokerOrderId = result.OrderId;
            message = result.Message;
        }
        catch (Exception)
        {
            // A network error cannot prove whether the broker accepted the order.
        }

        await store.Finish(proposal.Id, state, brokerOrderId, message, CancellationToken.None);
        return OrderView.From(
            proposal with
            {
                State = state,
                BrokerOrderId = brokerOrderId,
                Message = message,
            },
            time.GetUtcNow()
        );
    }
}
