namespace Quantara.Portfolio.Domain;

public sealed record OrderProposal(
    Guid Id,
    string Symbol,
    TradeSide Side,
    decimal Amount,
    decimal LimitPrice,
    decimal ReferencePrice,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    OrderState State,
    string? BrokerOrderId,
    string? Message
)
{
    public decimal NotionalThb => this.Side == TradeSide.Buy ? this.Amount : this.Amount * this.LimitPrice;

    public decimal EstimatedFeeThb => decimal.Ceiling(this.NotionalThb * 0.0025m * 100m) / 100m;
}
