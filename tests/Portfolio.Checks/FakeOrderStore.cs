using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed class FakeOrderStore : IOrderStore
{
    private readonly object gate = new();

    private readonly Dictionary<Guid, OrderProposal> proposals = [];

    public bool FailFinish { get; set; }

    public bool FailFind { get; set; }

    public bool FailReviewedFind { get; set; }

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

    public Task<IReadOnlyList<OrderProposal>> Exposure(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<OrderProposal>>(
                this.proposals.Values.Where(proposal =>
                        proposal.State
                            is OrderState.Submitting
                                or OrderState.Unknown
                                or OrderState.Submitted
                                or OrderState.PartiallyFilled
                        || proposal.State == OrderState.Pending && proposal.ExpiresAt > now
                    )
                    .ToArray()
            );
        }
    }

    public Task<OrderProposal?> Find(Guid id, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            var proposal = this.proposals.GetValueOrDefault(id);
            if (this.FailFind || this.FailReviewedFind && proposal is not null && proposal.State != OrderState.Pending)
            {
                throw new InvalidOperationException("Reviewed order read unavailable fixture");
            }

            return Task.FromResult(proposal);
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

    public Task UpdateObserved(
        Guid id,
        OrderState state,
        string? brokerOrderId,
        string? message,
        CancellationToken cancellationToken
    )
    {
        lock (this.gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.FailFinish)
            {
                throw new InvalidOperationException("Fixture storage failure");
            }

            var proposal =
                this.proposals.GetValueOrDefault(id) ?? throw new InvalidOperationException("Order missing.");
            if (
                proposal.State
                is not (
                    OrderState.Submitting
                    or OrderState.Unknown
                    or OrderState.Submitted
                    or OrderState.PartiallyFilled
                )
            )
            {
                throw new InvalidOperationException("Only submitted or uncertain orders can be reconciled.");
            }

            this.proposals[id] = proposal with
            {
                State =
                    proposal.State == OrderState.PartiallyFilled && state == OrderState.Submitted
                        ? OrderState.PartiallyFilled
                        : state,
                BrokerOrderId = brokerOrderId ?? proposal.BrokerOrderId,
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
