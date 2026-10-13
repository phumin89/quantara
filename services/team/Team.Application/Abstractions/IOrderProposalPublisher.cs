namespace Quantara.Team.Application;

public interface IOrderProposalPublisher
{
    public Task Publish(Guid runId, string accountKey, TradeProposal proposal, CancellationToken cancellationToken);
}
