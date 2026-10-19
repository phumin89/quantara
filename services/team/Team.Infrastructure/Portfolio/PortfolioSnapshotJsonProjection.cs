using System.Text.Json;

namespace Quantara.Team.Infrastructure;

internal static class PortfolioSnapshotJsonProjection
{
    internal static decimal? OptionalDecimal(JsonElement value, string property)
    {
        var number = value.GetProperty(property);
        return number.ValueKind == JsonValueKind.Null ? null : number.GetDecimal();
    }

    internal static object ToTradeEvidence(JsonElement item) =>
        new
        {
            transactionId = item.GetProperty("transactionId").GetString(),
            symbol = item.GetProperty("symbol").GetString(),
            side = item.GetProperty("side").GetString(),
            quantity = item.GetProperty("quantity").GetDecimal(),
            price = item.GetProperty("price").GetDecimal(),
            fee = item.GetProperty("fee").GetDecimal(),
            feeCredit = item.GetProperty("feeCredit").GetDecimal(),
            timestamp = item.GetProperty("timestamp").Clone(),
        };

    internal static decimal? ReadNumber(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.Number
        && item.TryGetDecimal(out var number)
        && number is >= -1_000_000_000_000m and <= 1_000_000_000_000m
            ? number
            : null;

    internal static bool? ReadBool(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? item.GetBoolean()
            : null;

    internal static string? ReadText(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() is { Length: <= 512 } text
                ? text
                : null
            : null;

    internal static decimal? ReadBoundedDecimalString(JsonElement value, string property)
    {
        if (
            !value.TryGetProperty(property, out var item)
            || item.ValueKind != JsonValueKind.String
            || !decimal.TryParse(
                item.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number
            )
        )
        {
            return null;
        }

        return number is >= -1_000_000_000_000m and <= 1_000_000_000_000m ? number : null;
    }

    internal static int? ReadInteger(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.Number
        && item.TryGetInt32(out var number)
        && number is >= 0 and <= 1000
            ? number
            : null;
}
