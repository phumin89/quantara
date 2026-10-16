using System.Text.Json;
using System.Text.Json.Serialization;
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
        };
        options.Converters.Add(new JsonStringEnumConverter<TradingMode>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<TradingAction>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new TradeSideJsonConverter());
        return options;
    }
}
