using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Infrastructure;

public sealed class BitkubOrderInspector(HttpClient http, BitkubCredentials credentials, TimeProvider? time = null)
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
        return new(state, id, $"Verified with Bitkub: {state}. This check never places or resends an order.")
        {
            Fills = ReadFills(order, proposal, state, (time ?? TimeProvider.System).GetUtcNow()),
        };
    }

    private static RiskVerifiedFill[]? ReadFills(
        JsonElement order,
        OrderProposal proposal,
        OrderState state,
        DateTimeOffset now
    )
    {
        if (!order.TryGetProperty("history", out var history))
        {
            if (proposal.Sizing is not null)
            {
                throw new InvalidDataException("Verified fill and fee history is unavailable.");
            }

            return null;
        }

        if (history.GetArrayLength() > 100000)
        {
            throw new InvalidDataException("Fill history exceeded the evidence budget.");
        }

        var fills = new Dictionary<string, RiskVerifiedFill>(StringComparer.Ordinal);
        decimal filledAmount = 0;
        foreach (var row in history.EnumerateArray())
        {
            var transactionId = row.GetProperty("txn_id").GetString();
            var amount = Number(row, "amount");
            var price = Number(row, "rate");
            var fee = Number(row, "fee");
            var credit = Number(row, "credit");
            var at = DateTimeOffset.FromUnixTimeMilliseconds(row.GetProperty("timestamp").GetInt64());
            if (
                string.IsNullOrWhiteSpace(transactionId)
                || transactionId.Length > 128
                || amount <= 0
                || price <= 0
                || fee < 0
                || credit < 0
                || credit > fee
                || at < proposal.CreatedAt.AddSeconds(-5)
                || at > now.AddSeconds(5)
            )
            {
                throw new InvalidDataException("Invalid verified fill or fee evidence.");
            }

            var fill = new RiskVerifiedFill(
                transactionId,
                proposal.Side == TradeSide.Buy ? amount / price : amount,
                price,
                fee,
                credit,
                at
            );
            if (!fills.TryAdd(transactionId, fill))
            {
                if (fills[transactionId] != fill)
                {
                    throw new InvalidDataException("Conflicting fill transaction identity.");
                }

                continue;
            }

            filledAmount += amount;
        }

        if (
            filledAmount > proposal.Amount
            || state is OrderState.Filled or OrderState.PartiallyFilled && fills.Count == 0
        )
        {
            throw new InvalidDataException("Fill history does not match the reviewed order amount or state.");
        }

        return fills.Values.ToArray();
    }

    private static decimal Number(JsonElement row, string field) =>
        decimal.Parse(row.GetProperty(field).ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);

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
