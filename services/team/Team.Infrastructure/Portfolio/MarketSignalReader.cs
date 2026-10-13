using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class MarketSignalReader(HttpClient http, TimeProvider time) : IMarketSignalReader
{
    public async Task<MarketSignalNotice?> Read(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("api/market/signals", cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 250000)
        {
            throw new InvalidDataException("Market signal response is too large.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var now = time.GetUtcNow();
        var checkedAt = root.GetProperty("checkedAt").GetDateTimeOffset();
        if (
            !root.GetProperty("available").GetBoolean()
            || !root.GetProperty("connected").GetBoolean()
            || checkedAt > now.AddSeconds(5)
            || now - checkedAt > TimeSpan.FromMinutes(2)
        )
        {
            return null;
        }

        var signals = root.GetProperty("signals");
        if (signals.GetArrayLength() is < 1 or > 5)
        {
            return null;
        }

        var ids = new List<string>();
        var descriptions = new List<string>();
        foreach (var signal in signals.EnumerateArray())
        {
            var id = signal.GetProperty("id").GetString();
            var symbol = signal.GetProperty("symbol").GetString();
            var direction = signal.GetProperty("direction").GetString();
            var candleAt = signal.GetProperty("candleAt").GetInt64();
            var detectedAt = signal.GetProperty("detectedAt").GetDateTimeOffset();
            var price = signal.GetProperty("price").GetDecimal();
            if (
                string.IsNullOrWhiteSpace(id)
                || id.Length > 200
                || symbol is null
                || symbol.Length is < 5 or > 34
                || !symbol.EndsWith("_THB", StringComparison.Ordinal)
                || !symbol[..^4].All(char.IsAsciiLetterOrDigit)
                || direction is not ("breakout" or "breakdown")
                || price <= 0
                || candleAt > now.ToUnixTimeSeconds() - 300
                || candleAt < now.ToUnixTimeSeconds() - 600
                || detectedAt > now.AddSeconds(5)
                || now - detectedAt > TimeSpan.FromMinutes(2)
            )
            {
                throw new InvalidDataException("Market signal evidence is invalid or stale.");
            }

            ids.Add(id);
            descriptions.Add(
                symbol
                    + " "
                    + direction
                    + " at closed 5-minute bar "
                    + candleAt.ToString(CultureInfo.InvariantCulture)
                    + ", close "
                    + price.ToString(CultureInfo.InvariantCulture)
                    + " THB"
            );
        }

        var key = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", ids.Order(StringComparer.Ordinal)))))
            .ToLowerInvariant();
        return new MarketSignalNotice(key, string.Join("; ", descriptions), checkedAt);
    }
}
