using System.Text.Json;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public static class TradingIntentPersistence
{
    public static void Validate(TradingIntent previous, TradingIntent next)
    {
        if (
            next.Id != previous.Id
            || next.Symbol != previous.Symbol
            || next.Side != previous.Side
            || next.Amount != previous.Amount
            || next.LimitPrice != previous.LimitPrice
            || next.CreatedAt != previous.CreatedAt
            || next.Candle != previous.Candle
            || next.RulesHash != previous.RulesHash
            || next.Reason != previous.Reason
            || next.OriginalAmount != previous.OriginalAmount
            || next.ReplacesIntentId != previous.ReplacesIntentId
            || JsonSerializer.Serialize(next.Sizing, TradingJson.Options)
                != JsonSerializer.Serialize(previous.Sizing, TradingJson.Options)
        )
        {
            throw new InvalidDataException("The immutable trading intent was changed.");
        }
    }
}
