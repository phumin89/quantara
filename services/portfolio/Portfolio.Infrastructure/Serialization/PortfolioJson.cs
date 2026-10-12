using System.Text.Json;

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
        options.Converters.Add(new PositionStatusJsonConverter());
        options.Converters.Add(new CostStatusJsonConverter());
        options.Converters.Add(new TradeSideJsonConverter());
    }
}
