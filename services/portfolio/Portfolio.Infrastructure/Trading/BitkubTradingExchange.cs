using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class BitkubTradingExchange(HttpClient http, BitkubCredentials credentials, IOrderBroker broker)
    : ITradingExchange
{
    public Task<OrderMarket> Market(string symbol, TradeSide side, CancellationToken cancellationToken) =>
        broker.Market(symbol, side, cancellationToken);

    public Task<Balance[]> Balances(CancellationToken cancellationToken) => broker.Balances(cancellationToken);

    public Task<IPreparedOrderSubmission> Prepare(TradingIntent intent, CancellationToken cancellationToken) =>
        broker.Prepare(Proposal(intent), cancellationToken);

    public async Task<IPreparedOrderSubmission> PrepareCancellation(
        TradingIntent intent,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(intent.BrokerOrderId))
        {
            throw new InvalidOperationException("Cancellation requires a verified broker order ID.");
        }

        var body = JsonSerializer.Serialize(
            new
            {
                sym = intent.Symbol.ToLowerInvariant() + "_thb",
                id = intent.BrokerOrderId,
                sd = intent.Side == TradeSide.Buy ? "buy" : "sell",
            }
        );
        var request = await this.Signed(HttpMethod.Post, "/api/v3/market/cancel-order", body, cancellationToken);
        return new PreparedTradingCancellation(http, request);
    }

    public async Task<TradingOrderObservation> Inspect(TradingIntent intent, CancellationToken cancellationToken)
    {
        var observed = await broker.Inspect(Proposal(intent), cancellationToken);
        if (observed.State == OrderState.Unknown || observed.BrokerOrderId is null)
        {
            return new("unknown", observed.BrokerOrderId, null, [], "Order outcome is uncertain.");
        }

        var side = intent.Side == TradeSide.Buy ? "buy" : "sell";
        var path =
            "/api/v3/market/order-info?sym="
            + Uri.EscapeDataString(intent.Symbol.ToLowerInvariant() + "_thb")
            + "&id="
            + Uri.EscapeDataString(observed.BrokerOrderId)
            + "&sd="
            + side;
        using var request = await this.Signed(HttpMethod.Get, path, null, cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (document.RootElement.GetProperty("error").GetInt32() != 0)
        {
            throw new InvalidDataException("Broker reconciliation was rejected.");
        }

        var order = document.RootElement.GetProperty("result");
        if (
            order.GetProperty("id").ToString() != observed.BrokerOrderId
            || Number(order, "amount") != intent.Amount
            || order.TryGetProperty("client_id", out var client)
                && client.ToString().Length > 0
                && client.ToString() != intent.Id.ToString("N")
        )
        {
            throw new InvalidDataException("Broker order identity mismatch.");
        }

        var history = order.GetProperty("history").EnumerateArray().ToArray();
        int? quantityScale =
            history.Length == 0
                ? null
                : (await broker.Market(intent.Symbol, TradeSide.Sell, cancellationToken)).AmountScale;
        if (quantityScale is < 0 or > 18)
        {
            throw new InvalidDataException("Broker base precision is invalid.");
        }

        var fills = new List<TradingFill>();
        foreach (var row in history)
        {
            var identifier = row.GetProperty("txn_id").ToString();
            var amount = Number(row, "amount");
            var price = Number(row, "rate");
            var fee = Number(row, "fee");
            var credit = Number(row, "credit");
            var at = DateTimeOffset.FromUnixTimeMilliseconds(row.GetProperty("timestamp").GetInt64());
            if (
                identifier.Length is < 1 or > 150
                || amount <= 0
                || price <= 0
                || fee < 0
                || credit < 0
                || credit > fee
                || at < intent.CreatedAt.AddMinutes(-1)
                || at > DateTimeOffset.UtcNow.AddSeconds(5)
            )
            {
                throw new InvalidDataException("Broker fill evidence is invalid.");
            }

            var quantity = intent.Side == TradeSide.Buy ? amount / price : amount;
            var gross = intent.Side == TradeSide.Buy ? amount : amount * price;
            var id = new Guid(
                SHA256.HashData(Encoding.UTF8.GetBytes(intent.Id.ToString("N") + ":" + identifier)).AsSpan(0, 16)
            );
            fills.Add(
                new TradingFill(
                    id,
                    intent.Id,
                    identifier,
                    intent.Symbol,
                    intent.Side,
                    quantity,
                    price,
                    gross,
                    fee - credit,
                    at,
                    GrossFeeThb: fee,
                    FeeCreditThb: credit
                )
                {
                    QuantityScale = quantityScale,
                }
            );
        }

        var remaining = Number(order, "remaining");
        var reportedFilled = Number(order, "filled");
        var filledAmount =
            intent.Side == TradeSide.Buy ? fills.Sum(fill => fill.GrossThb) : fills.Sum(fill => fill.Quantity);
        var spent = intent.Side == TradeSide.Buy ? filledAmount + fills.Sum(fill => fill.FeeThb) : filledAmount;
        if (
            decimal.Abs(reportedFilled - filledAmount) > 0.00000001m
            && decimal.Abs(reportedFilled - spent) > 0.00000001m
        )
        {
            throw new InvalidDataException("Broker fill history does not cover the reported filled amount.");
        }

        if (
            remaining < 0
            || remaining > intent.Amount
            || fills.Select(fill => fill.BrokerFillId).Distinct(StringComparer.Ordinal).Count() != fills.Count
        )
        {
            throw new InvalidDataException("Broker remaining amount or fill identity is invalid.");
        }

        var state = order.GetProperty("status").GetString() switch
        {
            "filled" when remaining == 0 && fills.Count > 0 => "filled",
            "cancelled" => "cancelled",
            "unfilled" when order.GetProperty("partial_filled").GetBoolean() => "partial",
            "unfilled" => "submitted",
            _ => throw new InvalidDataException("Broker order outcome is unsupported."),
        };
        return new(state, observed.BrokerOrderId, remaining, fills);
    }

    private async Task<HttpRequestMessage> Signed(
        HttpMethod method,
        string path,
        string? body,
        CancellationToken cancellationToken
    )
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
                    Encoding.UTF8.GetBytes(timestamp + method.Method + path + body)
                )
            )
            .ToLowerInvariant();
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        request.Headers.Add("X-BTK-APIKEY", credentials.Key);
        request.Headers.Add("X-BTK-TIMESTAMP", timestamp);
        request.Headers.Add("X-BTK-SIGN", signature);
        return request;
    }

    private static decimal Number(JsonElement element, string name) =>
        decimal.Parse(
            element.GetProperty(name).ToString(),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture
        );

    private static OrderProposal Proposal(TradingIntent intent) =>
        new(
            intent.Id,
            intent.Symbol,
            intent.Side,
            intent.Amount,
            intent.LimitPrice,
            intent.LimitPrice,
            intent.Reason,
            intent.CreatedAt,
            DateTimeOffset.MaxValue,
            OrderState.Submitted,
            intent.BrokerOrderId,
            null
        );
}
