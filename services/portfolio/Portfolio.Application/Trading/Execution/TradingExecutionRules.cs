using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal static class TradingExecutionRules
{
    internal static bool Fresh(TradingBook book, TradingRuleSet rules, DateTimeOffset now, bool entry = true) =>
        (rules.Adaptive is not null && !entry ? book.SellEnabled ?? book.Enabled : book.Enabled)
        && book.Bid > 0
        && book.Ask >= book.Bid
        && book.PriceStep > 0
        && book.BaseScale is >= 0 and <= 18
        && book.QuoteScale is >= 0 and <= 18
        && book.PriceScale is >= 0 and <= 18
        && book.ObservedAt <= now
        && now - book.ObservedAt <= TimeSpan.FromSeconds(rules.MaxDataAgeSeconds)
        && (rules.Adaptive is not null && !entry || (book.Ask - book.Bid) / book.Bid * 10000m <= rules.MaxSpreadBps);

    internal static decimal Floor(decimal value, decimal step) =>
        step > 0 ? Math.Floor(value / step) * step : throw new InvalidDataException("Invalid market precision.");

    internal static decimal Scale(int scale)
    {
        if (scale is < 0 or > 18)
        {
            throw new InvalidDataException("Invalid market scale.");
        }

        var step = 1m;
        for (var i = 0; i < scale; i++)
        {
            step /= 10;
        }

        return step;
    }

    internal static OrderProposal Proposal(TradingIntent intent) =>
        new(
            intent.Id,
            intent.Symbol,
            intent.Side,
            intent.Amount,
            intent.LimitPrice,
            intent.LimitPrice,
            intent.Reason,
            intent.CreatedAt,
            DateTimeOffset.MaxValue,
            OrderState.Submitted,
            intent.BrokerOrderId,
            null
        );
}
