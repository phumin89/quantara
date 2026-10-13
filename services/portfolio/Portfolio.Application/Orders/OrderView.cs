using System.Globalization;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record OrderView(
    Guid Id,
    string Symbol,
    TradeSide Side,
    string Amount,
    string LimitPrice,
    string NotionalThb,
    string EstimatedFeeThb,
    string ReferencePrice,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    string? BrokerOrderId,
    string? Message
)
{
    public static OrderView From(OrderProposal proposal, DateTimeOffset now) =>
        new(
            proposal.Id,
            proposal.Symbol,
            proposal.Side,
            proposal.Amount.ToString(CultureInfo.InvariantCulture),
            proposal.LimitPrice.ToString(CultureInfo.InvariantCulture),
            proposal.NotionalThb.ToString(CultureInfo.InvariantCulture),
            proposal.EstimatedFeeThb.ToString(CultureInfo.InvariantCulture),
            proposal.ReferencePrice.ToString(CultureInfo.InvariantCulture),
            proposal.Reason,
            proposal.CreatedAt,
            proposal.ExpiresAt,
            (proposal.State == OrderState.Pending && now >= proposal.ExpiresAt ? OrderState.Expired : proposal.State)
                .ToString()
                .ToLowerInvariant(),
            proposal.BrokerOrderId,
            proposal.Message
        );
}
