using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakeOrderProposalPublisher : IOrderProposalPublisher
{
    public List<(Guid RunId, string AccountKey, TradeProposal Proposal)> Calls { get; } = [];

    public bool Fail { get; set; }

    public Task Publish(Guid runId, string accountKey, TradeProposal proposal, CancellationToken cancellationToken)
    {
        if (this.Fail)
        {
            throw new InvalidOperationException("publisher failed");
        }

        this.Calls.Add((runId, accountKey, proposal));
        return Task.CompletedTask;
    }
}
