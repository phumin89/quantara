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
    private const int maximumHistoryPages = 3;

    private const int maximumOrderInfoReads = 8;

    private const string incompleteSearch =
        "Broker search was incomplete. Outcome remains unknown; no order was resent.";

    public async Task<OrderInspection> Inspect(OrderProposal proposal, CancellationToken cancellationToken)
    {
        var (evidence, failure) = await this.FindOrder(proposal, cancellationToken);
        if (evidence is not { } order)
        {
            return new(OrderState.Unknown, null, failure);
        }

        if (
            !DecimalPrecision.MatchesPositive(order, "amount", proposal.Amount)
            || !DecimalPrecision.MatchesPositive(order, "rate", proposal.LimitPrice)
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
        return new(
            state,
            order.GetProperty("id").ToString(),
            $"Verified with Bitkub: {state}. This check never places or resends an order."
        )
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
                || proposal.Side == TradeSide.Buy && price > proposal.LimitPrice
                || proposal.Side == TradeSide.Sell && price < proposal.LimitPrice
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

        var remaining = Number(order, "remaining");
        var reportedFilled = Number(order, "filled");
        var spent =
            proposal.Side == TradeSide.Buy
                ? filledAmount + fills.Values.Sum(fill => fill.FeeThb - fill.FeeCreditThb)
                : filledAmount;
        if (
            reportedFilled < 0
            || decimal.Abs(reportedFilled - filledAmount) > 0.00000001m
                && decimal.Abs(reportedFilled - spent) > 0.00000001m
        )
        {
            throw new InvalidDataException("Broker fill history does not cover the reported filled amount.");
        }

        if (
            remaining < 0
            || remaining > proposal.Amount
            || filledAmount > proposal.Amount
            || state == OrderState.Filled && remaining != 0
            || state is OrderState.Filled or OrderState.PartiallyFilled && fills.Count == 0
        )
        {
            throw new InvalidDataException("Fill history does not match the reviewed order amount or state.");
        }

        return fills.Values.ToArray();
    }

    private static decimal Number(JsonElement row, string field) =>
        DecimalPrecision.Read(row.GetProperty(field), NumberStyles.Float);

    private async Task<(JsonElement? Order, string? Failure)> FindOrder(
        OrderProposal proposal,
        CancellationToken cancellationToken
    )
    {
        var cache = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (proposal.BrokerOrderId is { } knownId)
        {
            var known = await this.ResolveOrder(proposal, knownId, cache, cancellationToken);
            return (known, known is null ? incompleteSearch : null);
        }

        var pair = Uri.EscapeDataString(proposal.Symbol.ToLowerInvariant() + "_thb");
        var side = proposal.Side == TradeSide.Buy ? "buy" : "sell";
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var open = await this.Read($"/api/v3/market/my-open-orders?sym={pair}", cancellationToken);
        if (!Collect(open.GetProperty("result"), proposal, "id", side, candidates))
        {
            return (null, incompleteSearch);
        }

        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        var complete = false;
        for (var page = 0; page < maximumHistoryPages; page++)
        {
            var cursorQuery = cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor);
            var history = await this.Read(
                $"/api/v3/market/my-order-history?sym={pair}&pagination_type=keyset&lmt=100{cursorQuery}",
                cancellationToken
            );
            if (
                !Collect(history.GetProperty("result"), proposal, "order_id", side, candidates)
                || !history.TryGetProperty("pagination", out var pagination)
                || !pagination.TryGetProperty("has_next", out var hasNext)
                || hasNext.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            )
            {
                return (null, incompleteSearch);
            }

            if (!hasNext.GetBoolean())
            {
                complete = true;
                break;
            }

            if (!pagination.TryGetProperty("cursor", out var nextValue) || nextValue.ValueKind != JsonValueKind.String)
            {
                return (null, incompleteSearch);
            }

            var next = nextValue.GetString();
            if (string.IsNullOrWhiteSpace(next) || !cursors.Add(next))
            {
                return (null, incompleteSearch);
            }

            cursor = next;
        }

        if (!complete)
        {
            return (null, incompleteSearch);
        }

        JsonElement? match = null;
        foreach (var candidate in candidates.Order(StringComparer.Ordinal))
        {
            if (await this.ResolveOrder(proposal, candidate, cache, cancellationToken) is not { } order)
            {
                return (null, incompleteSearch);
            }

            if (match is { } previous && previous.GetProperty("id").ToString() != order.GetProperty("id").ToString())
            {
                return (
                    null,
                    "Several broker orders match this proposal. Outcome remains unknown; no order was resent."
                );
            }

            match = order;
        }

        return (
            match,
            match is null ? "No exact broker match found. Outcome remains unknown; no order was resent." : null
        );
    }

    private async Task<JsonElement?> ResolveOrder(
        OrderProposal proposal,
        string id,
        Dictionary<string, JsonElement> cache,
        CancellationToken cancellationToken
    )
    {
        if (await this.ReadOrder(proposal, id, cache, cancellationToken) is not { } order)
        {
            return null;
        }

        if (
            order.TryGetProperty("first", out var first)
            && !string.IsNullOrWhiteSpace(first.ToString())
            && first.ToString() != "0"
            && first.ToString() != id
        )
        {
            id = first.ToString();
            if (await this.ReadOrder(proposal, id, cache, cancellationToken) is not { } original)
            {
                return null;
            }

            order = original;
            if (
                order.TryGetProperty("first", out var root)
                && !string.IsNullOrWhiteSpace(root.ToString())
                && root.ToString() != "0"
                && root.ToString() != id
            )
            {
                throw new InvalidDataException("Broker order lineage did not resolve to an original order.");
            }
        }

        return order;
    }

    private async Task<JsonElement?> ReadOrder(
        OrderProposal proposal,
        string id,
        Dictionary<string, JsonElement> cache,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidDataException("Broker order identity did not match the proposal.");
        }

        if (cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        if (cache.Count >= maximumOrderInfoReads)
        {
            return null;
        }

        var pair = Uri.EscapeDataString(proposal.Symbol.ToLowerInvariant() + "_thb");
        var side = proposal.Side == TradeSide.Buy ? "buy" : "sell";
        var data = await this.Read(
            $"/api/v3/market/order-info?sym={pair}&id={Uri.EscapeDataString(id)}&sd={side}",
            cancellationToken
        );
        var order = data.GetProperty("result");
        // Verify every requested response before trusting its reference to an original order.
        if (
            order.GetProperty("id").ToString() != id
            || order.TryGetProperty("client_id", out var client)
                && client.ToString().Length > 0
                && client.ToString() != proposal.Id.ToString("N")
        )
        {
            throw new InvalidDataException("Broker order identity did not match the proposal.");
        }

        cache.Add(id, order);
        return order;
    }

    private static bool Collect(
        JsonElement rows,
        OrderProposal proposal,
        string idField,
        string side,
        HashSet<string> candidates
    )
    {
        foreach (
            var row in rows.EnumerateArray()
                .Where(row =>
                    row.TryGetProperty("client_id", out var client)
                    && client.ToString() == proposal.Id.ToString("N")
                    && row.GetProperty("side").GetString() == side
                )
        )
        {
            var id = row.GetProperty(idField).ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("Broker candidate identity is invalid.");
            }

            candidates.Add(id);
            if (candidates.Count > maximumOrderInfoReads)
            {
                return false;
            }
        }

        return true;
    }

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
