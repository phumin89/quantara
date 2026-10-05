using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record AiCompanyOrderIntent(
    OrderSide Side,
    decimal Quantity,
    string Thesis,
    string Invalidation,
    decimal Confidence);

public static class AiCompanyProposalSizer
{
    public static AiCompanyOrderIntent? Size(
        AiCompanyProposal proposal,
        PaperPosition? position,
        decimal price,
        decimal equity)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (price <= 0m || equity <= 0m) return null;

        return proposal.Action switch
        {
            PortfolioAction.Hold => null,
            PortfolioAction.Buy when position is null => Intent(
                OrderSide.Buy,
                TradeQuantity.FloorFor(proposal.Symbol, equity * proposal.TargetWeight / price)),
            PortfolioAction.Sell when position is not null => Intent(OrderSide.Sell, position.Quantity),
            PortfolioAction.Trim when position is not null => Intent(
                OrderSide.Sell,
                position.Quantity - TradeQuantity.FloorFor(proposal.Symbol, equity * proposal.TargetWeight / price)),
            _ => null
        };

        AiCompanyOrderIntent? Intent(OrderSide side, decimal quantity)
            => quantity > 0m
                ? new(side, quantity, proposal.Thesis, proposal.Invalidation, proposal.Confidence)
                : null;
    }
}
