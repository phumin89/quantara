using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Risk;

namespace Portfolio.Checks;

public sealed class FakeRiskStore : IRiskStore
{
    public RiskPolicyState Policy { get; set; } = RiskPolicyHash.Default();

    public CapitalSnapshot? Capital { get; private set; }

    public List<RiskSizingSnapshot> Decisions { get; } = [];

    public List<RiskVerifiedFill> Fills { get; } = [];

    public bool FailFills { get; set; }

    public TaskCompletionSource? BeforeObserve { get; set; }

    public Task<RiskPolicyState> ReadPolicy(CancellationToken cancellationToken) => Task.FromResult(this.Policy);

    public Task<CapitalSnapshot?> ReadCapital(CancellationToken cancellationToken) => Task.FromResult(this.Capital);

    public Task<RiskPerformance?> ReadPerformance(CancellationToken cancellationToken) =>
        Task.FromResult<RiskPerformance?>(null);

    public Task Configure(RiskPolicy policy, string expectedHash, CancellationToken cancellationToken)
    {
        if (expectedHash != this.Policy.PolicyHash)
        {
            throw new InvalidOperationException("Fixture policy conflict.");
        }

        this.Policy = new(RiskPolicyHash.Compute(policy), policy);
        return Task.CompletedTask;
    }

    public async Task Observe(
        CapitalSnapshot snapshot,
        IReadOnlyList<CapitalCashFlow> cashFlows,
        DateTimeOffset coverageFrom,
        CancellationToken cancellationToken
    )
    {
        if (this.BeforeObserve is { } pending)
        {
            await pending.Task.WaitAsync(cancellationToken);
        }

        this.Capital = snapshot;
    }

    public Task RecordSizing(Guid orderId, RiskSizingSnapshot snapshot, CancellationToken cancellationToken)
    {
        this.Decisions.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task RecordFills(Guid orderId, IReadOnlyList<RiskVerifiedFill> fills, CancellationToken cancellationToken)
    {
        if (this.FailFills)
        {
            throw new InvalidOperationException("Fixture accounting unavailable.");
        }

        this.Fills.AddRange(fills);
        return Task.CompletedTask;
    }
}
