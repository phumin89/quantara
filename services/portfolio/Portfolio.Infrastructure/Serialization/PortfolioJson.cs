using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Infrastructure;

public static class PortfolioJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            RespectRequiredConstructorParameters = true,
        };
        ConfigureOptions(options);
        return options;
    }

    public static void ConfigureOptions(JsonSerializerOptions options)
    {
        options.TypeInfoResolver = TradingJsonCompatibility.CreateResolver();
        options.Converters.Add(new PositionStatusJsonConverter());
        options.Converters.Add(new CostStatusJsonConverter());
        options.Converters.Add(new TradeSideJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter<RiskProfile>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<CapitalSource>(JsonNamingPolicy.CamelCase, false));
    }
}
