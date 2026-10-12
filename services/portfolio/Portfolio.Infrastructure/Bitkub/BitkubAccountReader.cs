using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

// This adapter only sends GET requests to the documented read endpoints.
public sealed class BitkubAccountReader(HttpClient http, BitkubCredentials credentials)
{
    public async Task<Balance[]> Balances(CancellationToken cancellationToken)
    {
        using var json = await this.Get("/api/v4/wallet/balances", true, cancellationToken);
        var balances = json.RootElement.GetProperty("data").EnumerateArray()
            .Select(item =>
            {
                var total = Number(item, "total");
                var available = Number(item, "available");
                if (total < 0 || available < 0 || available > total)
                {
                    throw new InvalidDataException("Unexpected negative or inconsistent wallet balance.");
                }

                return new Balance(Text(item, "currency"), total, available);
            }).ToArray();
        return balances;
    }

    public async Task<Trade[]> History(string symbol, CancellationToken cancellationToken)
    {
        var trades = new List<Trade>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
            var path = $"/api/v3/market/my-order-history?sym={Uri.EscapeDataString(symbol + "_THB")}&lmt=100&pagination_type=keyset";
            if (cursor is not null)
            {
                path += "&cursor=" + Uri.EscapeDataString(cursor);
            }

            using var json = await this.Get(path, true, cancellationToken);
            foreach (var item in json.RootElement.GetProperty("result").EnumerateArray())
            {
                trades.Add(ParseTrade(item, symbol));
            }

            var pagination = json.RootElement.GetProperty("pagination");
            if (!pagination.GetProperty("has_next").GetBoolean())
            {
                break;
            }

            cursor = Text(pagination, "cursor");
            if (string.IsNullOrEmpty(cursor) || !cursors.Add(cursor))
            {
                throw new ExchangeReadException("Bitkub history pagination did not advance. Imported records are retained.");
            }

            if (cursors.Count >= 1000)
            {
                throw new ExchangeReadException("Bitkub history exceeded the import limit. Imported records are retained.");
            }
        } while (true);
        return trades.ToArray();
    }

    public static Trade ParseTrade(JsonElement item, string symbol)
    {
        var side = Text(item, "side");
        var price = Number(item, "rate");
        var amount = Number(item, "amount");
        if (side is not ("buy" or "sell") || price <= 0 || amount < 0)
        {
            throw new InvalidDataException("Unexpected Bitkub trade fields.");
        }
        // Bitkub amount is THB for buys and coin quantity for sells.
        var quantity = side == "buy" ? amount / price : amount;
        var transactionId = Text(item, "txn_id");
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            throw new InvalidDataException("Trade identifier is missing.");
        }

        return new(transactionId, symbol, side, quantity, price,
            Number(item, "fee"), Number(item, "credit"),
            DateTimeOffset.FromUnixTimeMilliseconds(item.GetProperty("ts").GetInt64()));
    }

    private async Task<JsonDocument> Get(string path, bool signed, CancellationToken cancellationToken)
    {
        if (signed && (string.IsNullOrWhiteSpace(credentials.Key) || string.IsNullOrWhiteSpace(credentials.Secret)))
        {
            throw new ExchangeReadException("Bitkub credentials are missing. Configure them on the backend.");
        }
        // Pace requests below the exchange limit, including server-time lookups.
        await Task.Delay(120, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (signed)
        {
            var timestamp = (await http.GetStringAsync("/api/v3/servertime", cancellationToken)).Trim();
            var input = timestamp + "GET" + path;
            var signature = Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(credentials.Secret), Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
            request.Headers.Add("X-BTK-APIKEY", credentials.Key);
            request.Headers.Add("X-BTK-TIMESTAMP", timestamp);
            request.Headers.Add("X-BTK-SIGN", signature);
        }
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExchangeReadException($"Bitkub read failed (HTTP {(int)response.StatusCode}). Check API permissions and IP access.");
        }

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        var code = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) ? error.ToString()
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var v4Code) ? v4Code.ToString() : "0";
        if (code == "0")
        {
            return json;
        }

        json.Dispose();
        throw new ExchangeReadException($"Bitkub rejected a read request (code {code}). Check API permissions and IP access.", code);
    }

    private static string Text(JsonElement item, string name) => item.GetProperty(name).ToString();
    private static decimal Number(JsonElement item, string name) => decimal.Parse(Text(item, name), NumberStyles.Float, CultureInfo.InvariantCulture);
}


