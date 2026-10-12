using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradeSideJsonConverter : JsonConverter<TradeSide>
{
    public override TradeSide Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString() switch
            {
                "buy" => TradeSide.Buy,
                "sell" => TradeSide.Sell,
                _ => throw new JsonException("Invalid trade side."),
            }
            : throw new JsonException("Trade side must be a string.");

    public override void Write(Utf8JsonWriter writer, TradeSide value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case TradeSide.Buy:
                writer.WriteStringValue("buy");
                break;
            case TradeSide.Sell:
                writer.WriteStringValue("sell");
                break;
            default:
                throw new JsonException("Invalid trade side.");
        }
    }
}
