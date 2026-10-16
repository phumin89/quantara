using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class FakePreparedTradingSubmission(FakeTradingExchange exchange, TradingIntent intent, bool cancellation)
    : IPreparedOrderSubmission
{
    public void Dispose() { }

    public Task<OrderBrokerResult> Send(CancellationToken cancellationToken) => exchange.Send(intent, cancellation);
}
