using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quantara.Portfolio.Infrastructure.Risk;

internal sealed class CompanyDecimalJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var value))
        {
            throw new JsonException("Capital amounts require exact decimal numbers.");
        }

        var text = reader.HasValueSequence
            ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
            : Encoding.UTF8.GetString(reader.ValueSpan);
        if (!DecimalPrecision.MatchesParsedNumber(text, value))
        {
            throw new JsonException("Capital amounts require exact decimal numbers.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
}
