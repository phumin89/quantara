using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record OrderInspection(OrderState State, string? BrokerOrderId, string? Message)
{
    public IReadOnlyList<RiskVerifiedFill>? Fills { get; init; }
}
