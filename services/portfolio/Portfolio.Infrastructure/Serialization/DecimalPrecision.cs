using System.Globalization;
using System.Text.Json;

namespace Quantara.Portfolio.Infrastructure;

internal static class DecimalPrecision
{
    internal static bool MatchesPositive(JsonElement order, string field, decimal expected) =>
        order.TryGetProperty(field, out var value)
        && expected > 0
        && TryRead(value, NumberStyles.Float, out var parsed)
        && parsed == expected;

    internal static decimal Read(JsonElement value, NumberStyles styles)
    {
        if (!TryRead(value, styles, out var parsed))
        {
            throw new InvalidDataException("Bitkub numeric evidence is not exactly representable.");
        }

        return parsed;
    }

    private static bool TryRead(JsonElement value, NumberStyles styles, out decimal parsed)
    {
        parsed = 0;
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
        {
            return false;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (text is null || !decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }

        return MatchesParsedNumber(text, parsed);
    }

    internal static bool MatchesParsedNumber(string text, decimal parsed)
    {
        // Callers validate their native number grammar first; parsing alone may round.
        if (text.Contains('\0'))
        {
            return false;
        }

        var parts = text.Trim().Split(['e', 'E']);
        var mantissa = parts[0].TrimStart('+', '-');
        var point = mantissa.IndexOf('.');
        var coefficient = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (coefficient.Length == 0)
        {
            return parsed == 0;
        }

        var exponent = 0;
        if (
            parts.Length == 2
            && !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)
        )
        {
            return false;
        }

        var digits = coefficient.TrimEnd('0');
        var scale =
            (long)(point < 0 ? 0 : mantissa.Length - point - 1) - exponent - (coefficient.Length - digits.Length);
        if (scale is < -28 or > 28)
        {
            return false;
        }

        string canonical;
        if (scale <= 0)
        {
            canonical = digits + new string('0', (int)-scale);
        }
        else if (scale >= digits.Length)
        {
            canonical = "0." + new string('0', (int)scale - digits.Length) + digits;
        }
        else
        {
            canonical = digits.Insert(digits.Length - (int)scale, ".");
        }

        if (parsed < 0)
        {
            canonical = "-" + canonical;
        }

        return StringComparer.Ordinal.Equals(
            canonical,
            parsed.ToString("0.############################", CultureInfo.InvariantCulture)
        );
    }
}
