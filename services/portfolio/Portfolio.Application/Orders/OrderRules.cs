using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public static class OrderRules
{
    public static void Validate(OrderProposal proposal, OrderMarket market)
    {
        if (
            !market.Enabled
            || market.ReferencePrice <= 0
            || market.PriceStep <= 0
            || market.MinimumNotional <= 0
            || market.PriceScale is < 0 or > 18
            || market.AmountScale is < 0 or > 18
        )
        {
            throw new OrderValidationException("This Bitkub THB spot market is unavailable.");
        }

        if (
            decimal.Round(proposal.Amount, market.AmountScale) != proposal.Amount
            || decimal.Round(proposal.LimitPrice, market.PriceScale) != proposal.LimitPrice
            || proposal.LimitPrice % market.PriceStep != 0
            || proposal.NotionalThb < market.MinimumNotional
        )
        {
            throw new OrderValidationException(
                "Amount or price does not meet the market precision, price step or minimum order value."
            );
        }
    }
}
