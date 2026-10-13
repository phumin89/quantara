using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed class FakeOrderBroker : IOrderBroker
{
    public decimal Price { get; set; } = 100m;

    public decimal Available { get; set; } = 1000m;

    public int Submissions { get; private set; }

    public bool FailSubmit { get; set; }

    public bool Rejected { get; set; }

    public OrderCheckTime? AdvanceOnBalance { get; set; }

    public TaskCompletionSource? Release { get; set; }

    public Task<OrderMarket> Market(string symbol, TradeSide side, CancellationToken cancellationToken) =>
        Task.FromResult(new OrderMarket(this.Price, 10m, 0.01m, 2, side == TradeSide.Buy ? 2 : 8, true));

    public Task<Balance[]> Balances(CancellationToken cancellationToken)
    {
        if (this.AdvanceOnBalance is not null)
        {
            this.AdvanceOnBalance.Now = this.AdvanceOnBalance.Now.AddMinutes(3);
        }

        return Task.FromResult<Balance[]>([new("THB", 1000m, this.Available), new("BTC", 1000m, this.Available)]);
    }

    public async Task<OrderBrokerResult> Submit(OrderProposal proposal, CancellationToken cancellationToken)
    {
        this.Submissions += 1;
        if (this.FailSubmit)
        {
            throw new HttpRequestException("Fixture uncertain result");
        }

        if (this.Release is not null)
        {
            await this.Release.Task.WaitAsync(cancellationToken);
        }

        return this.Rejected ? new(false, null, "Rejected fixture") : new(true, "broker-123", "Accepted, not filled");
    }
}
