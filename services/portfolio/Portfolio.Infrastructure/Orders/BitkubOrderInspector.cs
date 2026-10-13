using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class BitkubOrderInspector(HttpClient http, BitkubCredentials credentials)
{
    public async Task<OrderInspection> Inspect(OrderProposal proposal, CancellationToken cancellationToken)
    {
        var pair = Uri.EscapeDataString(proposal.Symbol.ToLowerInvariant() + "_thb");
        var side = proposal.Side == TradeSide.Buy ? "buy" : "sell";
        var id = proposal.BrokerOrderId;
        if (id is null)
        {
            var open = await this.Read($"/api/v3/market/my-open-orders?sym={pair}", cancellationToken);
            id = BitkubOrderInspector.Find(open.GetProperty("result"), proposal, "id", side);
            string? cursor = null;
            for (var page = 1; id is null && page <= 3; page++)
            {
                var cursorQuery = cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor);
                var history = await this.Read(
                    $"/api/v3/market/my-order-history?sym={pair}&pagination_type=keyset&lmt=100{cursorQuery}",
                    cancellationToken
                );
                var rows = history.GetProperty("result");
                id = BitkubOrderInspector.Find(rows, proposal, "order_id", side);
                if (
                    !history.TryGetProperty("pagination", out var pagination)
                    || !pagination.GetProperty("has_next").GetBoolean()
                )
                {
                    break;
                }

                var next = pagination.GetProperty("cursor").GetString();
                if (string.IsNullOrWhiteSpace(next) || next == cursor)
                {
                    break;
                }

                cursor = next;
            }
        }

        if (id is null)
        {
            return new(
                OrderState.Unknown,
                null,
                "No exact broker match found. Outcome remains unknown; no order was resent."
            );
        }

        var data = await this.Read(
            $"/api/v3/market/order-info?sym={pair}&id={Uri.EscapeDataString(id)}&sd={side}",
            cancellationToken
        );
        var order = data.GetProperty("result");
        if (
            order.TryGetProperty("first", out var first)
            && !string.IsNullOrWhiteSpace(first.ToString())
            && first.ToString() != "0"
            && first.ToString() != id
        )
        {
            id = first.ToString();
            data = await this.Read(
                $"/api/v3/market/order-info?sym={pair}&id={Uri.EscapeDataString(id)}&sd={side}",
                cancellationToken
            );
            order = data.GetProperty("result");
        }

        if (
            order.GetProperty("id").ToString() != id
            || (
                order.TryGetProperty("client_id", out var client)
                && client.ToString().Length > 0
                && client.ToString() != proposal.Id.ToString("N")
            )
        )
        {
            throw new InvalidDataException("Broker order identity did not match the proposal.");
        }

        var state = order.GetProperty("status").GetString() switch
        {
            "filled" => OrderState.Filled,
            "cancelled" => OrderState.Cancelled,
            "unfilled" when order.GetProperty("partial_filled").GetBoolean() => OrderState.PartiallyFilled,
            "unfilled" => OrderState.Submitted,
            _ => throw new InvalidDataException("Unknown broker order status."),
        };
        return new(state, id, $"Verified with Bitkub: {state}. This check never places or resends an order.");
    }

    private static string? Find(JsonElement rows, OrderProposal proposal, string idField, string side) =>
        rows.EnumerateArray()
            .Where(row =>
                row.TryGetProperty("client_id", out var client)
                && client.ToString() == proposal.Id.ToString("N")
                && row.GetProperty("side").GetString() == side
            )
            .Select(row => row.GetProperty(idField).ToString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .FirstOrDefault();

    private async Task<JsonElement> Read(string path, CancellationToken cancellationToken)
    {
        var timestamp = (await http.GetStringAsync("/api/v3/servertime", cancellationToken)).Trim();
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidDataException("Invalid broker timestamp.");
        }

        var signature = Convert
            .ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(credentials.Secret),
                    Encoding.UTF8.GetBytes(timestamp + "GET" + path)
                )
            )
            .ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-BTK-APIKEY", credentials.Key);
        request.Headers.Add("X-BTK-TIMESTAMP", timestamp);
        request.Headers.Add("X-BTK-SIGN", signature);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (document.RootElement.GetProperty("error").GetInt32() != 0)
        {
            throw new InvalidDataException("Broker status could not be verified. No order was sent.");
        }

        return document.RootElement.Clone();
    }
}
