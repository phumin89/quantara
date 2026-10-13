using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IOrderBroker
{
    public Task<OrderMarket> Market(string symbol, TradeSide side, CancellationToken cancellationToken);

    public Task<Balance[]> Balances(CancellationToken cancellationToken);

    public Task<OrderBrokerResult> Submit(OrderProposal proposal, CancellationToken cancellationToken);
}
