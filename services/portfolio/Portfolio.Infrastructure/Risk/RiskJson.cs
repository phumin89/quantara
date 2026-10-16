using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Infrastructure.Risk;

internal static class RiskJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = PortfolioJson.CreateOptions();
        options.RespectNullableAnnotations = true;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new JsonStringEnumConverter<CashFlowKind>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
