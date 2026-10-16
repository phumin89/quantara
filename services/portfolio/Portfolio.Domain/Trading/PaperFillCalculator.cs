namespace Quantara.Portfolio.Domain.Trading;

public static class PaperFillCalculator
{
    public static PaperExecution? Fill(
        TradeSide side,
        decimal remaining,
        decimal limitPrice,
        TradingBook book,
        TradingRuleSet rules,
        int baseScale
    )
    {
        if (remaining <= 0m || limitPrice <= 0m || baseScale is < 0 or > 18 || !Enum.IsDefined(side))
        {
            return null;
        }

        var price = side == TradeSide.Buy ? book.Ask : book.Bid;
        if (price <= 0)
        {
            return null;
        }

        if (side == TradeSide.Buy && price > limitPrice || side == TradeSide.Sell && price < limitPrice)
        {
            return null;
        }

        var available = side == TradeSide.Buy ? book.AskQuantity : book.BidQuantity;
        if (available <= 0m)
        {
            return null;
        }

        rules.Validate();
        var rate = rules.FeeBps / 10000m;
        var slice = Math.Min(remaining, available * price / (1m - rate));
        var fee = side == TradeSide.Buy ? Math.Ceiling(slice * rate * 100m) / 100m : 0;
        var quantity =
            side == TradeSide.Buy ? Math.Min(available, (slice - fee) / price) : Math.Min(available, remaining);
        quantity = Math.Floor(quantity * Pow10(baseScale)) / Pow10(baseScale);
        if (quantity <= 0m)
        {
            return null;
        }

        var gross = quantity * price;
        if (side == TradeSide.Sell)
        {
            fee = Math.Ceiling(gross * rate * 100m) / 100m;
        }

        if (side == TradeSide.Buy && gross + fee > remaining)
        {
            return null;
        }

        return new(quantity, side == TradeSide.Buy ? gross + fee : gross, fee);
    }

    private static decimal Pow10(int scale)
    {
        var value = 1m;
        for (var i = 0; i < scale; i++)
        {
            value *= 10m;
        }

        return value;
    }
}
