using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingExchange
{
    public Task<OrderMarket> Market(string symbol, TradeSide side, CancellationToken cancellationToken);

    public Task<Balance[]> Balances(CancellationToken cancellationToken);

    public Task<IPreparedOrderSubmission> Prepare(TradingIntent intent, CancellationToken cancellationToken);

    public Task<IPreparedOrderSubmission> PrepareCancellation(
        TradingIntent intent,
        CancellationToken cancellationToken
    );

    public Task<TradingOrderObservation> Inspect(TradingIntent intent, CancellationToken cancellationToken);
}
