using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed class FakeOrderStore : IOrderStore
{
    private readonly object gate = new();

    private readonly Dictionary<Guid, OrderProposal> proposals = [];

    public bool FailFinish { get; set; }

    public Task Create(OrderProposal proposal, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            this.proposals.Add(proposal.Id, proposal);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OrderProposal>> List(CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            return Task.FromResult<IReadOnlyList<OrderProposal>>(this.proposals.Values.ToArray());
        }
    }

    public Task<OrderProposal?> Find(Guid id, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            return Task.FromResult(this.proposals.GetValueOrDefault(id));
        }
    }

    public Task<bool> Claim(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            var proposal = this.proposals.GetValueOrDefault(id);
            if (
                proposal is null
                || proposal.State != OrderState.Pending
                || now >= proposal.ExpiresAt
                || this.proposals.Values.Any(item => item.State is OrderState.Submitting or OrderState.Unknown)
            )
            {
                return Task.FromResult(false);
            }

            this.proposals[id] = proposal with { State = OrderState.Submitting };
            return Task.FromResult(true);
        }
    }

    public Task Finish(
        Guid id,
        OrderState state,
        string? brokerOrderId,
        string? message,
        CancellationToken cancellationToken
    )
    {
        if (this.FailFinish)
        {
            throw new InvalidOperationException("Fixture storage failure");
        }

        lock (this.gate)
        {
            this.proposals[id] = this.proposals[id] with
            {
                State = state,
                BrokerOrderId = brokerOrderId,
                Message = message,
            };
        }

        return Task.CompletedTask;
    }

    public Task Reject(Guid id, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            if (this.proposals.TryGetValue(id, out var proposal) && proposal.State == OrderState.Pending)
            {
                this.proposals[id] = proposal with { State = OrderState.Rejected };
            }
        }

        return Task.CompletedTask;
    }
}
