using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed class FakeOrderBroker : IOrderBroker
{
    public decimal Price { get; set; } = 100m;

    public decimal Available { get; set; } = 1000m;

    public Balance[]? ExactBalances { get; set; }

    public int Submissions { get; private set; }

    internal void CountSubmission() => this.Submissions += 1;

    public bool FailSubmit { get; set; }

    public bool FailPrepare { get; set; }

    public OrderInspection Inspection { get; set; } = new(OrderState.Filled, "fixture-order", "Verified fixture");

    public Task<OrderInspection> Inspect(OrderProposal proposal, CancellationToken cancellationToken) =>
        Task.FromResult(this.Inspection);

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

        return Task.FromResult<Balance[]>(
            this.ExactBalances ?? [new("THB", 1000m, this.Available), new("BTC", 1000m, this.Available)]
        );
    }

    public Task<IPreparedOrderSubmission> Prepare(OrderProposal proposal, CancellationToken cancellationToken)
    {
        if (this.FailPrepare)
        {
            throw new HttpRequestException("Fixture preparation failure.");
        }

        return Task.FromResult<IPreparedOrderSubmission>(new Prepared(this));
    }

    public async Task<OrderBrokerResult> Submit(OrderProposal proposal, CancellationToken cancellationToken) =>
        await (await this.Prepare(proposal, cancellationToken)).Send(cancellationToken);
}
