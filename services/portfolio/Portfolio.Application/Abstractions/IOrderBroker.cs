using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IOrderBroker
{
    public Task<OrderMarket> Market(string symbol, TradeSide side, CancellationToken cancellationToken);

    public Task<Balance[]> Balances(CancellationToken cancellationToken);

    public Task<IPreparedOrderSubmission> Prepare(OrderProposal proposal, CancellationToken cancellationToken);

    public Task<OrderInspection> Inspect(OrderProposal proposal, CancellationToken cancellationToken) =>
        Task.FromResult(new OrderInspection(OrderState.Unknown, null, "Inspection is unavailable."));
}
