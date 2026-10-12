using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class CostStatusJsonConverter : JsonConverter<CostStatus>
{
    public override CostStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString() switch
            {
                "Unknown" => CostStatus.Unknown,
                _ => throw new JsonException("Invalid cost status."),
            }
            : throw new JsonException("Cost status must be a string.");

    public override void Write(Utf8JsonWriter writer, CostStatus value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case CostStatus.Unknown:
                writer.WriteStringValue("Unknown");
                break;
            default:
                throw new JsonException("Invalid cost status.");
        }
    }
}
