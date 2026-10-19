using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingPositionBalance
{
    public static async Task<bool> Covers(
        decimal? quantity,
        TradingHolding position,
        ITradingExchange exchange,
        CancellationToken cancellationToken,
        int? quantityScale = null
    )
    {
        if (quantity is null or < 0)
        {
            return false;
        }

        if (quantity >= position.Quantity)
        {
            return true;
        }

        var scale =
            quantityScale ?? (await exchange.Market(position.Symbol, TradeSide.Sell, cancellationToken)).AmountScale;
        if (scale is < 0 or > 18)
        {
            throw new InvalidDataException("Invalid market scale.");
        }

        return quantity >= decimal.Round(position.Quantity, scale, MidpointRounding.ToZero);
    }
}
