using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

// GET-only verification. Order identity comes from broker history, never from a browser-provided order ID.
public sealed class BitkubManualSaleEvidenceReader(
    BitkubAccountReader reader,
    IAccountContext account,
    TimeProvider time
) : IManualSaleEvidenceReader
{
    public async Task<ManualSaleBrokerEvidence> Read(
        string symbol,
        string executionId,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(symbol)
            || symbol.Length > 30
            || !symbol.All(char.IsAsciiLetterOrDigit)
            || symbol.Any(character => character is >= 'a' and <= 'z')
            || string.IsNullOrWhiteSpace(executionId)
            || executionId.Length > 150
        )
        {
            throw new ArgumentException("A supported symbol and exact broker execution identity are required.");
        }

        var row = await this.Find(symbol, executionId, cancellationToken);
        var trade = BitkubAccountReader.ParseTrade(row, symbol);
        if (
            trade.Side != TradeSide.Sell
            || trade.Quantity <= 0
            || trade.Fee < 0
            || trade.FeeCredit < 0
            || trade.FeeCredit > trade.Fee
        )
        {
            throw new InvalidDataException("The broker execution is not a supported confirmed sale.");
        }

        var orderId = row.GetProperty("order_id").ToString();
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 150)
        {
            throw new InvalidDataException("Broker history did not supply an order identity.");
        }

        var path =
            "/api/v3/market/order-info?sym="
            + Uri.EscapeDataString(symbol.ToLowerInvariant() + "_thb")
            + "&id="
            + Uri.EscapeDataString(orderId)
            + "&sd=sell";
        using var order = await reader.Get(path, true, cancellationToken);
        var reference = VerifyOrder(order.RootElement.GetProperty("result"), trade, orderId);
        var observedAt = time.GetUtcNow();
        var balance =
            (await reader.Balances(cancellationToken)).SingleOrDefault(item => item.Symbol == symbol)
            ?? throw new InvalidDataException("Current broker wallet evidence is missing; missing is not zero.");
        using var openOrders = await reader.Get(
            "/api/v3/market/my-open-orders?sym=" + Uri.EscapeDataString(symbol.ToLowerInvariant() + "_thb"),
            true,
            cancellationToken
        );
        var orders = openOrders.RootElement.GetProperty("result");
        var finalBalance = (await reader.Balances(cancellationToken)).SingleOrDefault(item => item.Symbol == symbol);
        if (
            orders.ValueKind != JsonValueKind.Array
            || orders.GetArrayLength() != 0
            || balance != finalBalance
            || time.GetUtcNow() - observedAt > TimeSpan.FromSeconds(30)
        )
        {
            throw new InvalidDataException(
                "A coherent fresh wallet and no open orders are required for inventory retirement."
            );
        }

        // History/order-info confirm the sale, but expose no explicit credited net THB field here.
        // Report the unrounded calculation separately; never promote it to a cash credit or infer it from wallet totals.
        return new ManualSaleBrokerEvidence(
            new ManualSaleExecution(
                account.AccountKey,
                executionId,
                orderId,
                symbol,
                TradeSide.Sell,
                trade.Quantity,
                trade.Timestamp,
                observedAt,
                reference
            ),
            balance,
            observedAt,
            ProceedsEstimate: EstimateProceeds(trade)
        );
    }

    public static ManualSaleProceedsEstimate EstimateProceeds(Trade trade)
    {
        if (
            trade.Side != TradeSide.Sell
            || trade.Quantity <= 0m
            || trade.Price <= 0m
            || trade.Fee < 0m
            || trade.FeeCredit < 0m
            || trade.FeeCredit > trade.Fee
        )
        {
            throw new InvalidDataException("Only a valid sell fill can supply a derived proceeds estimate.");
        }

        var gross = trade.Quantity * trade.Price;
        var fees = trade.Fee - trade.FeeCredit;
        return new ManualSaleProceedsEstimate(gross, fees, gross - fees);
    }

    public static string VerifyOrder(JsonElement order, Trade trade, string orderId)
    {
        var rows = order.GetProperty("history").EnumerateArray().ToArray();
        var matched = rows.SingleOrDefault(item => item.GetProperty("txn_id").ToString() == trade.TransactionId);
        if (
            trade.Side != TradeSide.Sell
            || order.GetProperty("id").ToString() != orderId
            || order.GetProperty("status").GetString() != "filled"
            || Number(order, "remaining") != 0m
            || order.TryGetProperty("client_id", out var client) && !string.IsNullOrWhiteSpace(client.ToString())
            || rows.Length is < 1 or > 1000
            || rows.Any(item => Number(item, "amount") <= 0)
            || matched.ValueKind != JsonValueKind.Object
            || rows.Select(item => item.GetProperty("txn_id").ToString()).Distinct(StringComparer.Ordinal).Count()
                != rows.Length
            || rows.Sum(item => Number(item, "amount")) != Number(order, "filled")
            || Number(order, "filled") != Number(order, "total")
            || Number(matched, "amount") != trade.Quantity
            || Number(matched, "rate") != trade.Price
            || Number(matched, "fee") != trade.Fee
            || Number(matched, "credit") != trade.FeeCredit
            || matched.GetProperty("timestamp").GetInt64() != trade.Timestamp.ToUnixTimeMilliseconds()
        )
        {
            throw new InvalidDataException(
                "Broker order and execution evidence did not agree; no attribution authorized."
            );
        }

        var evidence = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                version = 1,
                trade.Symbol,
                trade.TransactionId,
                orderId,
                trade.Quantity,
                trade.Price,
                trade.Fee,
                trade.FeeCredit,
                trade.Timestamp,
                status = "filled",
                filled = Number(order, "filled"),
                remaining = 0m,
            }
        );
        return "bitkub-sale-v1:" + Convert.ToHexString(SHA256.HashData(evidence)).ToLowerInvariant();
    }

    private async Task<JsonElement> Find(string symbol, string executionId, CancellationToken cancellationToken)
    {
        string? cursor = null;
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < 10; page++)
        {
            var path =
                "/api/v3/market/my-order-history?sym="
                + Uri.EscapeDataString(symbol + "_THB")
                + "&lmt=100&pagination_type=keyset";
            if (cursor is not null)
            {
                path += "&cursor=" + Uri.EscapeDataString(cursor);
            }

            using var document = await reader.Get(path, true, cancellationToken);
            var matched = document
                .RootElement.GetProperty("result")
                .EnumerateArray()
                .Where(item => item.GetProperty("txn_id").ToString() == executionId)
                .ToArray();
            if (matched.Length > 1)
            {
                throw new InvalidDataException("Broker execution identity is duplicated.");
            }

            if (matched.Length == 1)
            {
                return matched[0].Clone();
            }

            var pagination = document.RootElement.GetProperty("pagination");
            if (!pagination.GetProperty("has_next").GetBoolean())
            {
                break;
            }

            cursor = pagination.GetProperty("cursor").GetString();
            if (string.IsNullOrWhiteSpace(cursor) || !cursors.Add(cursor))
            {
                throw new InvalidDataException("Broker history pagination did not advance.");
            }
        }

        throw new InvalidDataException("Requested execution was not found within the bounded broker history read.");
    }

    private static decimal Number(JsonElement row, string name) =>
        DecimalPrecision.Read(row.GetProperty(name), NumberStyles.Float);
}
