using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class PositionStatusJsonConverter : JsonConverter<PositionStatus>
{
    public override PositionStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString() switch
            {
                "Holding" => PositionStatus.Holding,
                "Closed" => PositionStatus.Closed,
                _ => throw new JsonException("Invalid position status."),
            }
            : throw new JsonException("Position status must be a string.");

    public override void Write(Utf8JsonWriter writer, PositionStatus value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case PositionStatus.Holding:
                writer.WriteStringValue("Holding");
                break;
            case PositionStatus.Closed:
                writer.WriteStringValue("Closed");
                break;
            default:
                throw new JsonException("Invalid position status.");
        }
    }
}
