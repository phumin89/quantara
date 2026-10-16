using System.Text.Json.Serialization.Metadata;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingJsonCompatibility
{
    public static DefaultJsonTypeInfoResolver CreateResolver()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(Apply);
        return resolver;
    }

    private static void Apply(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            var name = property.Name.ToLowerInvariant();
            if (
                info.Type == typeof(TradingRuleSet) && name == "adaptive"
                || info.Type == typeof(TradingState) && name == "autonomy"
                || info.Type == typeof(TradingIntent) && name == "sizing"
                || info.Type == typeof(TradingHolding) && name is "stopprice" or "targetprice" or "trailingdistance"
                || info.Type == typeof(TradingBook) && name == "sellenabled"
            )
            {
                property.ShouldSerialize = (_, value) => value is not null;
            }
            else if (info.Type == typeof(TradingState) && name == "observedfundingflows")
            {
                property.ShouldSerialize = (_, value) => value is string[] { Length: > 0 };
            }
            else if (info.Type == typeof(TradingState) && name == "shadows")
            {
                property.ShouldSerialize = (_, value) => value is IReadOnlyList<TradingShadow> { Count: > 0 };
            }
            else if (info.Type == typeof(RiskMarketEvidence) && name == "resting")
            {
                property.ShouldSerialize = (_, value) => value is true;
            }
        }
    }
}
