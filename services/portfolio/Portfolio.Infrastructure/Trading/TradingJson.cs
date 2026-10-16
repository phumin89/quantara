using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

internal static class TradingJson
{
    public static JsonSerializerOptions Options { get; } = TradingJson.CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = TradingJsonCompatibility.CreateResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter<TradingMode>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<TradingAction>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<RiskProfile>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<TradingShadowState>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new TradeSideJsonConverter());
        return options;
    }
}
