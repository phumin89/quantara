using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.Infrastructure;

// Explicit cumulative wallet amounts from authenticated order_update. No user ID or auth material.
public sealed record BitkubOrderReceipt(string OrderId, string ClientId, string Symbol, OrderSide Side,
    string Status, decimal LimitPrice, decimal OrderAmount, decimal ExecutedAmount, decimal ReceivedAmount,
    decimal TotalFee, decimal CreditUsed, decimal CashFee, long UpdatedAt);

public static class BitkubOrderReceipts
{
    internal static BitkubOrderReceipt? Parse(JsonElement data)
    {
        try
        {
            // Manual orders are not adopted. REST/wallet reconciliation still detects them.
            var client = BitkubLiveBroker.Required(data, "client_id").GetString();
            if (client is null || !Guid.TryParseExact(client, "N", out var id) || id == Guid.Empty) return null;
            if (BitkubLiveBroker.Identifier(data, "type") != "limit") throw Invalid();
            var symbol = BitkubLiveBroker.Identifier(data, "symbol").ToUpperInvariant().Replace('_', '-');
            if (!LiveMarketSymbol.IsValid(symbol)) throw Invalid();
            var side = BitkubLiveBroker.Identifier(data, "side") switch
            { "buy" => OrderSide.Buy, "sell" => OrderSide.Sell, _ => throw Invalid() };
            var currency = side == OrderSide.Buy ? "THB" : symbol[..^4];
            var received = side == OrderSide.Buy ? symbol[..^4] : "THB";
            if (BitkubLiveBroker.Identifier(data, "order_currency") != currency ||
                BitkubLiveBroker.Identifier(data, "executed_currency") != currency ||
                BitkubLiveBroker.Identifier(data, "received_currency") != received) throw Invalid();
            var updated = BitkubLiveBroker.Required(data, "order_updated_at");
            var timestamp = (updated.ValueKind == JsonValueKind.Null
                ? BitkubLiveBroker.Required(data, "order_created_at") : updated).GetInt64();
            var status = BitkubLiveBroker.Identifier(data, "status");
            if (timestamp <= 0 || status is not ("new" or "open" or "partial_filled" or "filled" or
                "canceled" or "partial_filled_canceled" or "rejected")) throw Invalid();
            var receipt = new BitkubOrderReceipt(BitkubLiveBroker.Identifier(data, "order_id"), client, symbol, side, status,
                BitkubLiveBroker.Amount(data, "price"), BitkubLiveBroker.Amount(data, "order_amount"),
                BitkubLiveBroker.Amount(data, "executed_amount"), BitkubLiveBroker.Amount(data, "received_amount"),
                BitkubLiveBroker.Amount(data, "total_fee"), BitkubLiveBroker.Amount(data, "credit_used"),
                BitkubLiveBroker.Amount(data, "net_fee_paid"), timestamp);
            if (receipt.LimitPrice <= 0 || receipt.OrderAmount <= 0 || receipt.CreditUsed > receipt.TotalFee ||
                receipt.CashFee != receipt.TotalFee - receipt.CreditUsed) throw Invalid();
            return receipt;
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException or OverflowException)
        { throw Invalid(); }
    }

    internal static IReadOnlyList<BrokerFill> Normalize(LiveAccountId account, LiveIntent intent,
        BrokerOrderEvidence order, BitkubVerifiedExecutionRules rules, BitkubOrderReceipt receipt)
    {
        var statusMatches = order.Status switch
        {
            "filled" => receipt.Status == "filled",
            "cancelled" => receipt.Status is "canceled" or "partial_filled_canceled",
            _ => receipt.Status is "new" or "open" or "partial_filled"
        };
        if (receipt.ClientId != intent.Id.ToString("N") || receipt.OrderId != order.OrderId || receipt.Symbol != intent.Symbol ||
            receipt.Side != intent.Side || receipt.LimitPrice != intent.LimitPrice || receipt.OrderAmount != order.RawAmount ||
            !statusMatches || receipt.TotalFee != order.History.Sum(f => f.ReportedFee) ||
            receipt.CreditUsed != order.History.Sum(f => f.FeeCredit) || receipt.CashFee != receipt.TotalFee - receipt.CreditUsed ||
            order.History.Any(f => f.Timestamp > receipt.UpdatedAt || f.OrderId != order.OrderId) ||
            order.History.Select(f => f.TransactionId).Distinct(StringComparer.Ordinal).Count() != order.History.Count) throw Invalid();
        var matches = new List<BrokerFill[]>();
        // No global REST fee assumption. Each order is proven against explicit broker
        // cash debit/proceeds and received units, including orders paid entirely by credits.
        foreach (var convention in new[] { BitkubBuyHistoryAmount.QuoteBeforeCashFee, BitkubBuyHistoryAmount.QuoteIncludingCashFee })
        {
            try
            {
                var fills = order.History.Select(raw => BitkubReconciliationSource.Normalize(account, intent,
                    order.OrderId, raw, rules with { BuyHistoryAmount = convention })).ToArray();
                var units = fills.Sum(f => f.Quantity);
                var quote = fills.Sum(f => f.GrossQuoteAmount);
                var fees = fills.Sum(f => f.CashFee);
                if (fees == receipt.CashFee && (intent.Side == OrderSide.Buy
                    ? quote + fees == receipt.ExecutedAmount && units == receipt.ReceivedAmount
                    : units == receipt.ExecutedAmount && receipt.ReceivedAmount >= 0 &&
                        receipt.ReceivedAmount % .01m == 0 &&
                        Math.Abs(quote - fees - receipt.ReceivedAmount) < fills.Length * .01m)) matches.Add(fills);
            }
            catch (InvalidDataException) { /* Identity, fees, units and authenticated settlement must all verify. */ }
        }
        if (matches.Count == 0 || matches.Any(m => !m.SequenceEqual(matches[0]))) throw Invalid();
        return matches[0];
    }

    private static InvalidDataException Invalid() => new("BITKUB_ORDER_RECEIPT_UNVERIFIED");
}
