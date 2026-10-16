using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public interface IRiskSizingService
{
    public Task<RiskSizingSnapshot?> Size(
        Guid orderId,
        string symbol,
        TradeSide side,
        decimal requestedAmount,
        decimal limit,
        RiskSignal? signal,
        CancellationToken cancellationToken
    );

    public Task<CapitalSnapshot?> Revalidate(OrderProposal proposal, CancellationToken cancellationToken);
}
