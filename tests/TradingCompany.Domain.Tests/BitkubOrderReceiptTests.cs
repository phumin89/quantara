using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubOrderReceiptTests
{
    private static long Timestamp => LiveRiskTests.Now.ToUnixTimeMilliseconds();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Buy_receipt_proves_exact_cash_debit_and_received_coins_for_either_rest_convention(bool includesFee)
    {
        var intent = Intent();
        var order = Order(intent, [Raw(includesFee ? 100.25m : 100m, .25m)]);

        var fill = Assert.Single(Normalize(intent, order, Receipt(intent)));

        Assert.Equal(.001m, fill.Quantity);
        Assert.Equal(100m, fill.GrossQuoteAmount);
        Assert.Equal(.25m, fill.CashFee);
        Assert.Equal(100.25m, fill.GrossQuoteAmount + fill.CashFee);
    }

    [Fact]
    public void Fully_credited_buy_is_proven_when_both_conventions_produce_identical_fills()
    {
        var intent = Intent(100m);
        var raw = Raw(100m, .25m) with { FeeCredit = .25m };
        var order = Order(intent, [raw]);
        var receipt = Receipt(intent) with { ExecutedAmount = 100m, CreditUsed = .25m, CashFee = 0m };
        var before = BitkubReconciliationSource.Normalize(intent.AccountId, intent, order.OrderId, raw,
            Rules() with { BuyHistoryAmount = BitkubBuyHistoryAmount.QuoteBeforeCashFee });
        var including = BitkubReconciliationSource.Normalize(intent.AccountId, intent, order.OrderId, raw,
            Rules() with { BuyHistoryAmount = BitkubBuyHistoryAmount.QuoteIncludingCashFee });

        var proven = Assert.Single(Normalize(intent, order, receipt));

        Assert.Equal(before, including);
        Assert.Equal(before, proven);
        Assert.Equal(100m, proven.GrossQuoteAmount);
        Assert.Equal(.001m, proven.Quantity);
        Assert.Equal(0m, proven.CashFee);
    }

    [Fact]
    public void Sell_receipt_proves_explicit_net_cash_proceeds()
    {
        var intent = Intent() with { Side = OrderSide.Sell, Quantity = .001m, BuyBudget = null };
        var order = Order(intent, [Raw(.001m, .25m)]);
        var receipt = Receipt(intent) with { ExecutedAmount = .001m, ReceivedAmount = 99.75m };

        var fill = Assert.Single(Normalize(intent, order, receipt));

        Assert.Equal(.001m, fill.Quantity);
        Assert.Equal(100m, fill.GrossQuoteAmount);
        Assert.Equal(99.75m, fill.GrossQuoteAmount - fill.CashFee);
    }

    [Theory]
    [InlineData("29.12", "0.08", "0.08")]
    [InlineData("29.05", "0.08", "0")]
    [InlineData("29.13", "0", "0")]
    public void Sell_receipt_proves_cent_settlement_without_guessing_rounding_or_allocating_fill_cash(
        string receivedText, string feeText, string creditText)
    {
        var received = decimal.Parse(receivedText, System.Globalization.CultureInfo.InvariantCulture);
        var fee = decimal.Parse(feeText, System.Globalization.CultureInfo.InvariantCulture);
        var credit = decimal.Parse(creditText, System.Globalization.CultureInfo.InvariantCulture);
        var intent = Intent() with { Side = OrderSide.Sell, Quantity = .00001040m, LimitPrice = 2800287.93m, BuyBudget = null };
        var raw = Raw(intent.Quantity, fee) with { Rate = intent.LimitPrice, FeeCredit = credit };
        var receipt = Receipt(intent) with { ExecutedAmount = intent.Quantity, ReceivedAmount = received,
            TotalFee = fee, CreditUsed = credit, CashFee = fee - credit };

        var fill = Assert.Single(Normalize(intent, Order(intent, [raw]), receipt));

        Assert.Equal(intent.Quantity, fill.Quantity);
        Assert.Equal(intent.LimitPrice, fill.Price);
        Assert.Equal(fee - credit, fill.CashFee);
        Assert.Null(fill.QuoteAmount); // Actual cumulative cash belongs to the order receipt.
        Assert.Equal(29.122994472m, fill.GrossQuoteAmount);
    }

    [Fact]
    public void Multiple_sell_fills_keep_exact_prices_when_cumulative_receipt_has_cash_precision()
    {
        var intent = Intent() with { Side = OrderSide.Sell, Quantity = .00002m, LimitPrice = 2800000m, BuyBudget = null };
        var first = Raw(.00001m, 0m, "tx-1") with { Rate = 2800123.45m };
        var second = Raw(.00001m, 0m, "tx-2") with { Rate = 2800765.43m };
        var receipt = Receipt(intent) with { ExecutedAmount = intent.Quantity, ReceivedAmount = 56m,
            TotalFee = 0m, CreditUsed = 0m, CashFee = 0m };

        var fills = Normalize(intent, Order(intent, [first, second]), receipt);

        Assert.Equal(2, fills.Count);
        Assert.Equal(first.Rate, fills[0].Price);
        Assert.Equal(second.Rate, fills[1].Price);
        Assert.All(fills, fill => Assert.Null(fill.QuoteAmount));
    }

    [Theory]
    [InlineData("29.11")]
    [InlineData("29.14")]
    [InlineData("29.123")]
    [InlineData("-0.01")]
    public void Sell_receipt_rejects_excess_precision_difference_or_invalid_net_cash(string receivedText)
    {
        var intent = Intent() with { Side = OrderSide.Sell, Quantity = .00001040m, LimitPrice = 2800287.93m, BuyBudget = null };
        var order = Order(intent, [Raw(intent.Quantity, 0m) with { Rate = intent.LimitPrice }]);
        var receipt = Receipt(intent) with { ExecutedAmount = intent.Quantity,
            ReceivedAmount = decimal.Parse(receivedText, System.Globalization.CultureInfo.InvariantCulture),
            TotalFee = 0m, CreditUsed = 0m, CashFee = 0m };

        Assert.Throws<InvalidDataException>(() => Normalize(intent, order, receipt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settlement_bound_cannot_count_duplicate_or_foreign_fill_history(bool foreign)
    {
        var intent = Intent() with { Side = OrderSide.Sell, Quantity = .002m, BuyBudget = null };
        var first = Raw(.001m, 0m);
        var second = foreign ? first with { OrderId = "foreign-order", TransactionId = "tx-2" } : first;
        var receipt = Receipt(intent) with { ExecutedAmount = .002m, ReceivedAmount = 200m,
            TotalFee = 0m, CreditUsed = 0m, CashFee = 0m };

        Assert.Throws<InvalidDataException>(() => Normalize(intent, Order(intent, [first, second]), receipt));
    }

    [Fact]
    public void Rounded_received_coins_keep_exact_gross_quote_and_receipt_debit()
    {
        var intent = Intent(30m, 3_000_000m);
        var order = Order(intent, [Raw(30m, .075m) with { Rate = 3_000_000m }]);
        var receipt = Receipt(intent) with
        { ExecutedAmount = 30m, ReceivedAmount = .00000997m, TotalFee = .075m, CashFee = .075m };

        var fill = Assert.Single(Normalize(intent, order, receipt));

        Assert.Equal(.00000997m, fill.Quantity);
        Assert.Equal(29.925m, fill.QuoteAmount);
        Assert.Equal(30m, fill.GrossQuoteAmount + fill.CashFee);
        Assert.NotEqual(fill.Quantity * fill.Price, fill.GrossQuoteAmount);
    }

    [Fact]
    public void Partial_receipt_matches_complete_cumulative_fill_history()
    {
        var intent = Intent(200m);
        var order = PartialOrder(intent);
        var receipt = Receipt(intent) with { Status = "partial_filled" };

        var fills = Normalize(intent, order, receipt);

        Assert.Equal(2, fills.Count);
        Assert.Equal(.001m, fills.Sum(fill => fill.Quantity));
        Assert.Equal(100.25m, fills.Sum(fill => fill.GrossQuoteAmount + fill.CashFee));
    }

    [Theory]
    [InlineData("missing-fill")]
    [InlineData("stale")]
    [InlineData("wrong-status")]
    public void Partial_aggregate_requires_every_fill_and_a_current_matching_status(string defect)
    {
        var intent = Intent(200m);
        var order = PartialOrder(intent);
        var receipt = Receipt(intent) with { Status = "partial_filled" };
        if (defect == "missing-fill") order = order with { History = [order.History[0]] };
        if (defect == "stale") receipt = receipt with { UpdatedAt = Timestamp - 1 };
        if (defect == "wrong-status") receipt = receipt with { Status = "filled" };

        Assert.Throws<InvalidDataException>(() => Normalize(intent, order, receipt));
    }

    [Theory]
    [InlineData("client")]
    [InlineData("order")]
    [InlineData("symbol")]
    [InlineData("side")]
    [InlineData("limit")]
    [InlineData("original-size")]
    [InlineData("fee")]
    [InlineData("credit")]
    [InlineData("cash-fee")]
    [InlineData("received")]
    [InlineData("cash")]
    [InlineData("timestamp")]
    [InlineData("status")]
    public void Receipt_must_match_owned_order_identity_and_exact_cash_accounting(string defect)
    {
        var intent = Intent();
        var order = Order(intent, [Raw(100m, .25m)]);
        var receipt = Receipt(intent);
        receipt = defect switch
        {
            "client" => receipt with { ClientId = new string('a', 32) },
            "order" => receipt with { OrderId = "foreign-order" },
            "symbol" => receipt with { Symbol = "ETH-THB" },
            "side" => receipt with { Side = OrderSide.Sell },
            "limit" => receipt with { LimitPrice = receipt.LimitPrice + .01m },
            "original-size" => receipt with { OrderAmount = receipt.OrderAmount + .01m },
            "fee" => receipt with { TotalFee = .26m },
            "credit" => receipt with { CreditUsed = .01m, CashFee = .24m },
            "cash-fee" => receipt with { CashFee = .26m },
            "received" => receipt with { ReceivedAmount = .00100001m },
            "cash" => receipt with { ExecutedAmount = 100.26m },
            "timestamp" => receipt with { UpdatedAt = Timestamp - 1 },
            _ => receipt with { Status = "open" }
        };

        Assert.Throws<InvalidDataException>(() => Normalize(intent, order, receipt));
    }

    [Fact]
    public void Parser_preserves_exact_string_decimals_and_normalizes_symbol()
    {
        var data = JsonReceipt();
        data["status"] = "partial_filled";
        data["order_amount"] = "200.000000000000000000000001";
        data["executed_amount"] = "100.250000000000000000000001";

        var receipt = Parse(JsonSerializer.Serialize(data))!;

        Assert.Equal("BTC-THB", receipt.Symbol);
        Assert.Equal(200.000000000000000000000001m, receipt.OrderAmount);
        Assert.Equal(100.250000000000000000000001m, receipt.ExecutedAmount);
        Assert.Equal(.001m, receipt.ReceivedAmount);
        Assert.Equal(.25m, receipt.CashFee);
    }

    [Theory]
    [InlineData("client_id")]
    [InlineData("received_amount")]
    [InlineData("total_fee")]
    [InlineData("order_updated_at")]
    public void Parser_rejects_duplicate_essential_fields(string field)
    {
        var data = JsonReceipt();
        var json = JsonSerializer.Serialize(data);
        var duplicate = json[..^1] + ",\"" + field + "\":" + JsonSerializer.Serialize(data[field]) + "}";

        Assert.Throws<InvalidDataException>(() => Parse(duplicate));
    }

    [Theory]
    [InlineData("order_currency", "BTC")]
    [InlineData("executed_currency", "BTC")]
    [InlineData("received_currency", "THB")]
    [InlineData("symbol", "THB_BTC")]
    [InlineData("side", "sell")]
    [InlineData("type", "market")]
    [InlineData("executed_amount", "0.123456789012345678901234567890123456789")]
    [InlineData("net_fee_paid", "0.24")]
    public void Parser_rejects_wrong_units_side_or_inexact_money(string field, string value)
    {
        var data = JsonReceipt();
        data[field] = value;

        Assert.Throws<InvalidDataException>(() => Parse(JsonSerializer.Serialize(data)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("manual-client")]
    [InlineData("00000000000000000000000000000000")]
    public void Parser_ignores_manual_orders_without_an_owned_client_id(string? client)
    {
        var data = JsonReceipt();
        data["client_id"] = client;
        data["executed_amount"] = "not-owned-or-adopted";

        Assert.Null(Parse(JsonSerializer.Serialize(data)));
    }

    [Fact]
    public void New_order_timestamp_falls_back_to_creation_when_updated_timestamp_is_null()
    {
        var data = JsonReceipt();
        data["order_updated_at"] = null;

        Assert.Equal(Timestamp - 1000, Parse(JsonSerializer.Serialize(data))!.UpdatedAt);
    }

    [Theory]
    [InlineData(OrderSide.Buy, BitkubBuyHistoryAmount.AuthenticatedOrderReceipts)]
    [InlineData(OrderSide.Sell, BitkubBuyHistoryAmount.AuthenticatedOrderReceipts)]
    [InlineData(OrderSide.Buy, (BitkubBuyHistoryAmount)99)]
    public void Receipt_accounting_and_unknown_modes_cannot_use_plain_rest_normalization(OrderSide side, BitkubBuyHistoryAmount mode)
    {
        var intent = Intent() with { Side = side, BuyBudget = side == OrderSide.Buy ? 100.25m : null };

        Assert.Throws<InvalidDataException>(() => BitkubReconciliationSource.Normalize(intent.AccountId, intent,
            "order", Raw(side == OrderSide.Buy ? 100m : .001m, .25m), Rules() with { BuyHistoryAmount = mode }));
    }

    private static LiveIntent Intent(decimal budget = 100.25m, decimal price = 100_000m)
        => LiveRiskTests.Intent with { Quantity = budget / price, LimitPrice = price, BuyBudget = budget };
    private static BrokerOrderFillEvidence Raw(decimal amount, decimal fee, string transaction = "tx")
        => new("order", transaction, 100_000m, amount, fee, 0m, Timestamp);
    private static BrokerOrderEvidence Order(LiveIntent intent, IReadOnlyList<BrokerOrderFillEvidence> fills)
        => new(intent.Symbol, intent.Side, "order", "order", "order", "order", intent.Id.ToString("N"), false,
            intent.BuyBudget ?? intent.Quantity, intent.LimitPrice, fills.Sum(fill => fill.ReportedFee), fills.Sum(fill => fill.FeeCredit),
            fills.Sum(fill => fill.RawAmount), intent.BuyBudget ?? intent.Quantity, "filled", false, 0m, fills);
    private static BrokerOrderEvidence PartialOrder(LiveIntent intent)
        => Order(intent, [Raw(40m, .10m, "tx-1"), Raw(60m, .15m, "tx-2")]) with
        { Status = "unfilled", PartiallyFilled = true, RawRemaining = 100m };
    private static BitkubOrderReceipt Receipt(LiveIntent intent)
        => new("order", intent.Id.ToString("N"), intent.Symbol, intent.Side, "filled", intent.LimitPrice,
            intent.BuyBudget ?? intent.Quantity, 100.25m, .001m, .25m, 0m, .25m, Timestamp);
    private static BitkubVerifiedExecutionRules Rules()
        => new("synthetic-receipt-confirmation", new string('a', 64), .0035m, BitkubBuyHistoryAmount.AuthenticatedOrderReceipts,
            [new("BTC-THB", 1m, .00000001m, .01m, 10m, 0, 1m) { BuyBaseRounding = BitkubBuyBaseRounding.FloorToBaseStep }]);
    private static IReadOnlyList<BrokerFill> Normalize(LiveIntent intent, BrokerOrderEvidence order, BitkubOrderReceipt receipt)
        => BitkubOrderReceipts.Normalize(intent.AccountId, intent, order, Rules(), receipt);
    private static BitkubOrderReceipt? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return BitkubOrderReceipts.Parse(document.RootElement);
    }
    private static Dictionary<string, object?> JsonReceipt() => new()
    {
        ["client_id"] = Intent().Id.ToString("N"), ["order_id"] = "order", ["symbol"] = "BTC_THB", ["side"] = "buy",
        ["type"] = "limit", ["status"] = "filled", ["order_currency"] = "THB", ["executed_currency"] = "THB",
        ["received_currency"] = "BTC", ["price"] = "100000", ["order_amount"] = "100.25", ["executed_amount"] = "100.25",
        ["received_amount"] = "0.00100000", ["total_fee"] = "0.25", ["credit_used"] = "0", ["net_fee_paid"] = "0.25",
        ["order_updated_at"] = Timestamp, ["order_created_at"] = Timestamp - 1000
    };
}
