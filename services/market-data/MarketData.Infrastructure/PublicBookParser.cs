using System.Globalization;
using System.Text.Json;

namespace Quantara.MarketData.Infrastructure;

public static class PublicBookParser
{
    public static string? Parse(string json, int pairingId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (
                !root.TryGetProperty("event", out var type)
                || type.GetString() is not ("depthchanged" or "bidschanged" or "askschanged" or "tradeschanged")
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)
            )
            {
                return null;
            }

            // The socket URL binds the market. Some provider events omit pairing_id.
            if (
                root.TryGetProperty("pairing_id", out var pair)
                && (
                    !int.TryParse(pair.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var actual)
                    || actual != pairingId
                )
            )
            {
                return null;
            }

            return json;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
