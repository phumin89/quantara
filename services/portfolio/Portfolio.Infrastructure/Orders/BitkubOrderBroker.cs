using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class BitkubOrderBroker(HttpClient http, BitkubCredentials credentials, BitkubAccountReader reader)
    : IOrderBroker
{
    public Task<Balance[]> Balances(CancellationToken cancellationToken) => reader.Balances(cancellationToken);

    public async Task<OrderMarket> Market(string symbol, TradeSide side, CancellationToken cancellationToken)
    {
        using var symbols = await this.Read("/api/v3/market/symbols", cancellationToken);
        var target = symbol + "_THB";
        var matches = symbols
            .RootElement.GetProperty("result")
            .EnumerateArray()
            .Where(item =>
                item.GetProperty("symbol").GetString()?.Equals(target, StringComparison.OrdinalIgnoreCase) == true
            )
            .ToArray();
        if (matches.Length != 1)
        {
            throw new OrderValidationException("Unknown Bitkub THB spot symbol.");
        }

        var item = matches[0];
        var enabled =
            item.GetProperty("status").GetString() == "active"
            && item.GetProperty("market_segment").GetString() == "SPOT"
            && item.GetProperty("source").GetString() == "exchange"
            && !item.GetProperty(side == TradeSide.Buy ? "freeze_buy" : "freeze_sell").GetBoolean();
        using var ticker = await this.Read(
            $"/api/v3/market/ticker?sym={Uri.EscapeDataString(target.ToLowerInvariant())}",
            cancellationToken
        );
        var quotes = ticker
            .RootElement.EnumerateArray()
            .Where(quote =>
                quote.GetProperty("symbol").GetString()?.Equals(target, StringComparison.OrdinalIgnoreCase) == true
            )
            .ToArray();
        if (quotes.Length != 1)
        {
            throw new OrderValidationException("A fresh market price is unavailable.");
        }

        return new(
            BitkubOrderBroker.Number(quotes[0], "last"),
            BitkubOrderBroker.Number(item, "min_quote_size"),
            BitkubOrderBroker.Number(item, "price_step"),
            item.GetProperty("price_scale").GetInt32(),
            item.GetProperty(side == TradeSide.Buy ? "quote_asset_scale" : "base_asset_scale").GetInt32(),
            enabled
        );
    }

    public async Task<OrderBrokerResult> Submit(OrderProposal proposal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.Key) || string.IsNullOrWhiteSpace(credentials.Secret))
        {
            throw new InvalidOperationException("Broker credentials are unavailable.");
        }

        var path = proposal.Side == TradeSide.Buy ? "/api/v3/market/place-bid" : "/api/v3/market/place-ask";
        var body =
            $"{{\"sym\":\"{proposal.Symbol.ToLowerInvariant()}_thb\",\"amt\":{proposal.Amount.ToString("G29", CultureInfo.InvariantCulture)},\"rat\":{proposal.LimitPrice.ToString("G29", CultureInfo.InvariantCulture)},\"typ\":\"limit\",\"client_id\":\"{proposal.Id:N}\"}}";
        var timestamp = (await http.GetStringAsync("/api/v3/servertime", cancellationToken)).Trim();
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidDataException("Invalid broker timestamp.");
        }

        var signature = Convert
            .ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(credentials.Secret),
                    Encoding.UTF8.GetBytes(timestamp + "POST" + path + body)
                )
            )
            .ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-BTK-APIKEY", credentials.Key);
        request.Headers.Add("X-BTK-TIMESTAMP", timestamp);
        request.Headers.Add("X-BTK-SIGN", signature);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var error = json.RootElement.GetProperty("error").GetInt32();
        if (error != 0)
        {
            return new(
                false,
                null,
                $"Bitkub rejected the order (code {error}). Review market rules, balance and Trade API permission."
            );
        }

        var orderId = json.RootElement.GetProperty("result").GetProperty("id").ToString();
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 200)
        {
            throw new InvalidDataException("The broker did not return an order identifier.");
        }

        return new(
            true,
            orderId,
            "Accepted by Bitkub. Submitted does not mean filled. Manage the open limit order in Bitkub; approval expiry does not cancel it."
        );
    }

    private async Task<JsonDocument> Read(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (
            document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("error", out var error)
            && error.ToString() != "0"
        )
        {
            document.Dispose();
            throw new OrderValidationException("Bitkub market data is unavailable. No order was sent.");
        }

        return document;
    }

    private static decimal Number(JsonElement item, string property) =>
        decimal.Parse(item.GetProperty(property).ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
