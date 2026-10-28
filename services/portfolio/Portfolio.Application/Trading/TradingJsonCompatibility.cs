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
                info.Type == typeof(TradingRuleSet) && name is "adaptive" or "exitpolicy"
                || info.Type == typeof(TradingState)
                    && name is "autonomy" or "evaluation" or "aipolicy" or "policyreview"
                || info.Type == typeof(AutonomousTradingStatus) && name == "history"
                || info.Type == typeof(TradingIntent)
                    && name
                        is "sizing"
                            or "exitplan"
                            or "exitstudyplan"
                            or "lastinspectionerror"
                            or "lastexecutionmessage"
                            or "lastexecutionat"
                            or "originalamount"
                            or "replacesintentid"
                || info.Type == typeof(TradingHolding)
                    && name is "stopprice" or "targetprice" or "trailingdistance" or "exitplan"
                || info.Type == typeof(TradingAlert) && name is "claimedat" or "messageid" or "sentat" or "retryat"
                || info.Type == typeof(TradingBook) && name == "sellenabled"
            )
            {
                property.ShouldSerialize = (_, value) => value is not null;
            }
            else if (info.Type == typeof(TradingAlert) && name == "retrycount")
            {
                property.ShouldSerialize = (_, value) => value is int count && count > 0;
            }
            else if (info.Type == typeof(TradingLedger) && name == "externalsales")
            {
                property.ShouldSerialize = (_, value) => value is IReadOnlyList<TradingExternalSale> { Count: > 0 };
            }
            else if (info.Type == typeof(TradingLedger) && name == "cashbaselines")
            {
                property.ShouldSerialize = (_, value) => value is IReadOnlyList<TradingCashBaseline> { Count: > 0 };
            }
            else if (info.Type == typeof(TradingState) && name == "observedfundingflows")
            {
                property.ShouldSerialize = (_, value) => value is string[] { Length: > 0 };
            }
            else if (info.Type == typeof(TradingState) && name == "shadows")
            {
                property.ShouldSerialize = (_, value) => value is IReadOnlyList<TradingShadow> { Count: > 0 };
            }
            else if (info.Type == typeof(TradingState) && name == "exitstudies")
            {
                property.ShouldSerialize = (_, value) => value is IReadOnlyList<TradingExitStudySample> { Count: > 0 };
            }
            else if (info.Type == typeof(TradingState) && name == "policychanges")
            {
                property.ShouldSerialize = (_, value) => value is IReadOnlyList<TradingPolicyChange> { Count: > 0 };
            }
            else if (info.Type == typeof(RiskMarketEvidence) && name == "resting")
            {
                property.ShouldSerialize = (_, value) => value is true;
            }
            else if (
                name == "timeframeseconds"
                && info.Type is { } type
                && (
                    type == typeof(TradingBook)
                    || type == typeof(TradingDecisionRecord)
                    || type == typeof(RiskMarketEvidence)
                )
            )
            {
                property.ShouldSerialize = (_, value) => value is not 300;
            }
            else if (info.Type == typeof(TradingDecisionRecord) && name == "signalaction")
            {
                property.ShouldSerialize = (_, value) => value is not null;
            }
        }
    }
}
