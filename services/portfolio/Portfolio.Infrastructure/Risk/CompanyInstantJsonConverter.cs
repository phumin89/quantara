using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quantara.Portfolio.Infrastructure.Risk;

internal sealed class CompanyInstantJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        var explicitOffset =
            text is not null && (text.EndsWith('Z') || text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':');
        if (!explicitOffset || !reader.TryGetDateTimeOffset(out var value))
        {
            throw new JsonException("Capital evidence requires an ISO timestamp with offset.");
        }

        return value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime());
}
