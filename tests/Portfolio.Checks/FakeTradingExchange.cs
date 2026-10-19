using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class FakeTradingExchange : ITradingExchange
{
    public bool FailSubmission { get; set; }

    public bool FailCancellation { get; set; }

    public int Submissions { get; private set; }

    public int Cancellations { get; private set; }

    public decimal AvailableCash { get; set; } = 2000m;

    public decimal AvailableBase { get; set; } = 20m;

    public Func<TradingIntent, TradingOrderObservation>? Observation { get; set; }

    public Action<TradingIntent, bool>? BeforeSend { get; set; }

    public Action? BeforeMarket { get; set; }

    public Task<OrderMarket> Market(string symbol, TradeSide side, CancellationToken cancellationToken)
    {
        this.BeforeMarket?.Invoke();
        return Task.FromResult(new OrderMarket(110, 10, .01m, 2, side == TradeSide.Buy ? 2 : 8, true));
    }

    public Task<Balance[]> Balances(CancellationToken cancellationToken) =>
        Task.FromResult<Balance[]>([
            new("THB", this.AvailableCash, this.AvailableCash),
            new("BTC", this.AvailableBase, this.AvailableBase),
            new("ETH", this.AvailableBase, this.AvailableBase),
        ]);

    public Task<IPreparedOrderSubmission> Prepare(TradingIntent intent, CancellationToken cancellationToken) =>
        Task.FromResult<IPreparedOrderSubmission>(new FakePreparedTradingSubmission(this, intent, false));

    public Task<IPreparedOrderSubmission> PrepareCancellation(
        TradingIntent intent,
        CancellationToken cancellationToken
    ) => Task.FromResult<IPreparedOrderSubmission>(new FakePreparedTradingSubmission(this, intent, true));

    public Task<TradingOrderObservation> Inspect(TradingIntent intent, CancellationToken cancellationToken) =>
        Task.FromResult(
            this.Observation?.Invoke(intent)
                ?? new TradingOrderObservation("submitted", intent.BrokerOrderId, intent.Remaining, [])
        );

    public Task<OrderBrokerResult> Send(TradingIntent intent, bool cancellation)
    {
        this.BeforeSend?.Invoke(intent, cancellation);
        if (cancellation)
        {
            this.Cancellations += 1;
        }
        else
        {
            this.Submissions += 1;
        }

        if (cancellation ? this.FailCancellation : this.FailSubmission)
        {
            throw new HttpRequestException("Fake broker outcome is unknown.");
        }

        return Task.FromResult(new OrderBrokerResult(true, "fake-" + intent.Id.ToString("N"), null));
    }
}
