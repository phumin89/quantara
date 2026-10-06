using System.Text.Json;
using System.Text.Json.Nodes;
using TradingCompany.Infrastructure;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubProfileVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("before")]
    [InlineData("bought")]
    public void Receipt_profile_keeps_cash_or_owned_BTC_without_a_manual_sale(string stage)
    {
        var capture = JsonNode.Parse(Json(Capture(stage)))!;
        SetObservedAt(capture, Now.AddSeconds(-1));
        var profile = BitkubProfileVerifier.ForOrderReceipts(Encoding(capture.ToJsonString()), Fingerprint, Now);
        Assert.Equal(BitkubBuyHistoryAmount.AuthenticatedOrderReceipts, profile.Rules.BuyHistoryAmount);
        Assert.True(profile.AccountBindingVerified);
        Assert.True(profile.TradePermissionVerified);
        Assert.Single(profile.Rules.Pairs);
        Assert.Equal("BTC-THB", profile.Rules.Pairs.Single().Symbol);
        Assert.Equal(1m, profile.Rules.Pairs.Single().BuyQuoteStep);
        Assert.Equal(.00000001m, profile.Rules.Pairs.Single().SellBaseStep);
    }

    [Fact]
    public void Receipt_profile_accepts_funded_coin_only_wallet_with_zero_THB()
    {
        var capture = JsonNode.Parse(Json(Capture("bought")))!;
        SetWallet(capture, "THB", 0m);
        SetObservedAt(capture, Now.AddSeconds(-1));
        Assert.Equal(BitkubBuyHistoryAmount.AuthenticatedOrderReceipts,
            BitkubProfileVerifier.ForOrderReceipts(Encoding(capture.ToJsonString()), Fingerprint, Now).Rules.BuyHistoryAmount);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("permission")]
    [InlineData("unstable")]
    [InlineData("enabled")]
    [InlineData("opens")]
    [InlineData("minimum")]
    [InlineData("quantity-unit")]
    [InlineData("unsupported")]
    [InlineData("empty")]
    public void Receipt_profile_requires_current_supported_funded_observations(string defect)
    {
        var capture = JsonNode.Parse(Json(Capture("before")))!;
        SetObservedAt(capture, Now.AddSeconds(-1));
        switch (defect)
        {
            case "stale": SetObservedAt(capture, Now.AddMinutes(-6)); break;
            case "permission": capture["tradePermissionConfirmed"] = false; break;
            case "unstable": capture["stableWallet"] = false; break;
            case "enabled": capture["liveEnabled"] = true; break;
            case "opens": capture["openOrderCount"] = 1; break;
            case "minimum": capture["marketRules"]![0]!["MinimumQuoteSize"] = 1m; break;
            case "quantity-unit": capture["marketRules"]![0]!["QuantityScale"] = 8; break;
            case "unsupported": capture["walletBalances"]!.AsArray().Add(new JsonObject
                { ["Currency"] = "DOGE", ["Available"] = 1m, ["Reserved"] = 0m, ["Total"] = 1m }); break;
            case "empty": SetWallet(capture, "THB", 0m); break;
        }
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.ForOrderReceipts(Encoding(capture.ToJsonString()), Fingerprint, Now));
    }

    [Fact]
    public void Valid_buy_sell_canary_selects_cash_fee_and_floor_rounding()
    {
        var profile = Verify();
        Assert.True(profile.AccountBindingVerified);
        Assert.True(profile.TradePermissionVerified);
        Assert.Equal(BitkubBuyHistoryAmount.QuoteIncludingCashFee, profile.Rules.BuyHistoryAmount);
        Assert.Equal(BitkubBuyBaseRounding.FloorToBaseStep, profile.Rules.Pairs.Single().BuyBaseRounding);
        Assert.Equal(64, profile.EvidenceHash.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filled_market_canary_uses_actual_fill_prices_and_paid_fee_wallets(bool multipleBuyFills)
    {
        var bought = MarketCapture("bought");
        var sold = MarketCapture("sold");
        if (multipleBuyFills)
            foreach (var capture in new[] { bought, sold }) SplitBuyFill(capture);

        var profile = BitkubProfileVerifier.Verify(Encoding(Json(Capture("before"))),
            Encoding(bought.ToJsonString()), Encoding(sold.ToJsonString()), Fingerprint, Now);

        Assert.True(profile.AccountBindingVerified);
        Assert.True(profile.TradePermissionVerified);
        Assert.Equal(BitkubBuyHistoryAmount.QuoteIncludingCashFee, profile.Rules.BuyHistoryAmount);
        Assert.Equal(BitkubBuyBaseRounding.FloorToBaseStep, profile.Rules.Pairs.Single().BuyBaseRounding);
    }

    [Fact]
    public void Baseline_and_its_market_metadata_can_be_eight_hours_old()
    {
        var before = JsonNode.Parse(Json(Capture("before")))!;
        SetObservedAt(before, Now.AddHours(-8));

        var profile = BitkubProfileVerifier.Verify(Encoding(before.ToJsonString()),
            Encoding(MarketCapture("bought").ToJsonString()), Encoding(MarketCapture("sold").ToJsonString()), Fingerprint, Now);

        Assert.True(profile.AccountBindingVerified);
        Assert.Equal(BitkubBuyHistoryAmount.QuoteIncludingCashFee, profile.Rules.BuyHistoryAmount);
    }

    [Theory]
    [InlineData("buy", "unknown", 0)]
    [InlineData("sell", "unknown", 0)]
    [InlineData("buy", "limit", 0)]
    [InlineData("sell", "limit", 0)]
    [InlineData("buy", "market", 3_000_000)]
    [InlineData("sell", "market", 3_000_000)]
    [InlineData("buy", "market", -1)]
    public void Order_rate_must_match_the_history_type(string side, string type, int orderRate)
    {
        var bought = MarketCapture("bought");
        var sold = MarketCapture("sold");
        var index = side == "buy" ? 0 : 1;
        foreach (var capture in side == "buy" ? new[] { bought, sold } : new[] { sold })
        {
            capture["orderDetails"]![index]!["Rate"] = orderRate;
            capture["orderHistory"]![index]!["Type"] = type;
        }
        AssertRejected(Json(Capture("before")), bought.ToJsonString(), sold.ToJsonString());
    }

    [Fact]
    public void Mixed_history_types_in_one_market_order_are_rejected()
    {
        var bought = MarketCapture("bought");
        var sold = MarketCapture("sold");
        foreach (var capture in new[] { bought, sold })
        {
            SplitBuyFill(capture);
            capture["orderHistory"]![1]!["Type"] = "limit";
        }
        AssertRejected(Json(Capture("before")), bought.ToJsonString(), sold.ToJsonString());
    }

    [Theory]
    [InlineData("buy", 0)]
    [InlineData("sell", 0)]
    [InlineData("buy", -1)]
    [InlineData("sell", -1)]
    public void Market_order_requires_positive_actual_fill_prices(string side, int fillRate)
    {
        var bought = MarketCapture("bought");
        var sold = MarketCapture("sold");
        var index = side == "buy" ? 0 : 1;
        foreach (var capture in side == "buy" ? new[] { bought, sold } : new[] { sold })
        {
            capture["orderHistory"]![index]!["Rate"] = fillRate;
            capture["orderDetails"]![index]!["History"]![0]!["Rate"] = fillRate;
        }
        AssertRejected(Json(Capture("before")), bought.ToJsonString(), sold.ToJsonString());
    }

    [Fact]
    public void Actual_market_fill_price_must_respect_the_price_quantum()
    {
        var bought = MarketCapture("bought");
        var sold = MarketCapture("sold");
        foreach (var capture in new[] { bought, sold })
        {
            capture["orderHistory"]![0]!["Rate"] = 3_000_000.001m;
            capture["orderDetails"]![0]!["History"]![0]!["Rate"] = 3_000_000.001m;
        }
        AssertRejected(Json(Capture("before")), bought.ToJsonString(), sold.ToJsonString());
    }

    [Theory]
    [InlineData("buy")]
    [InlineData("sell")]
    public void Market_fill_must_occur_after_the_prior_capture(string side)
    {
        var bought = MarketCapture("bought");
        var sold = MarketCapture("sold");
        var index = side == "buy" ? 0 : 1;
        var timestamp = Now.AddMinutes(side == "buy" ? -2 : -1).ToUnixTimeMilliseconds();
        foreach (var capture in side == "buy" ? new[] { bought, sold } : new[] { sold })
        {
            capture["orderHistory"]![index]!["Timestamp"] = timestamp;
            capture["orderDetails"]![index]!["History"]![0]!["Timestamp"] = timestamp;
        }
        AssertRejected(Json(Capture("before")), bought.ToJsonString(), sold.ToJsonString());
    }

    [Fact]
    public void Verified_profile_roundtrips_through_strict_runtime_contract()
    {
        var profile = Verify();
        var json = JsonSerializer.Serialize(new BitkubLiveVerification(profile.BrokerAccountReference,
            profile.CredentialFingerprint, profile.ConfirmationReference, profile.EvidenceHash,
            profile.AccountBindingVerified, profile.TradePermissionVerified, profile.Rules));
        var restored = JsonSerializer.Deserialize<BitkubLiveVerification>(json,
            new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
        Assert.Equal(profile.EvidenceHash, restored!.EvidenceHash);
        Assert.Equal(BitkubBuyBaseRounding.FloorToBaseStep, restored.Rules.Pairs.Single().BuyBaseRounding);
    }

    [Theory]
    [InlineData("credentialFingerprint", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("liveEnabled", "true")]
    [InlineData("openOrderCount", "1")]
    public void Mutated_capture_is_rejected(string field, string value)
    {
        var before = Json(Capture("before"));
        var bought = Json(Capture("bought"));
        var sold = Json(Capture("sold"));
        var changed = field == "credentialFingerprint" ? before.Replace(Fingerprint, value) : before.Replace("\"" + field + "\":false", "\"" + field + "\":" + value).Replace("\"" + field + "\":0", "\"" + field + "\":" + value);
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(Encoding(changed), Encoding(bought), Encoding(sold), Fingerprint, Now));
    }

    [Fact]
    public void Evidence_hash_is_deterministic()
        => Assert.Equal(Verify().EvidenceHash, Verify().EvidenceHash);

    [Theory]
    [InlineData("100", "101")]
    public void Wallet_or_time_mutations_are_rejected(string oldValue, string newValue)
    {
        var before = Json(Capture("before")).Replace(oldValue, newValue);
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(Encoding(before), Encoding(Json(Capture("bought"))), Encoding(Json(Capture("sold"))), Fingerprint, Now));
    }

    [Theory]
    [InlineData("buy-tx", "missing-tx")]
    [InlineData("3000000", "3000001")]
    public void Retained_history_or_market_mutation_is_rejected(string oldValue, string newValue)
    {
        var sold = Json(Capture("sold")).Replace(oldValue, newValue);
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(Encoding(Json(Capture("before"))), Encoding(Json(Capture("bought"))), Encoding(sold), Fingerprint, Now));
    }

    [Fact]
    public void Empty_or_same_stage_capture_is_rejected()
    {
        var before = Encoding(Json(Capture("before")));
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(before, before, Encoding(Json(Capture("sold"))), Fingerprint, Now));
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(ReadOnlyMemory<byte>.Empty, before, before, Fingerprint, Now));
    }

    [Fact]
    public void Duplicate_root_property_is_rejected()
    {
        var json = Json(Capture("before")).Replace("{\"observedAt\"", "{\"observedAt\":\"2026-10-06T11:58:00+00:00\",\"observedAt\"");
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(Encoding(json), Encoding(Json(Capture("bought"))), Encoding(Json(Capture("sold"))), Fingerprint, Now));
    }

    [Theory]
    [InlineData(-2160)]
    [InlineData(-2161)]
    [InlineData(1)]
    public void Stale_or_future_capture_is_rejected(int hours)
    {
        var capture = JsonNode.Parse(Json(Capture("before")))!;
        SetObservedAt(capture, Now.AddHours(hours));
        AssertRejected(capture.ToJsonString(), Json(Capture("bought")), Json(Capture("sold")));
    }

    [Fact]
    public void Historical_canary_baseline_does_not_expire_while_the_owner_completes_the_round_trip()
    {
        var before = JsonNode.Parse(Json(Capture("before")))!;
        SetObservedAt(before, Now.AddDays(-30));
        var profile = BitkubProfileVerifier.Verify(Encoding(before.ToJsonString()),
            Encoding(Json(Capture("bought"))), Encoding(Json(Capture("sold"))), Fingerprint, Now);
        Assert.True(profile.AccountBindingVerified);
        Assert.Equal(BitkubBuyHistoryAmount.QuoteIncludingCashFee, profile.Rules.BuyHistoryAmount);
    }

    [Theory]
    [InlineData("History", null)]
    [InlineData("RawFilled", "0")]
    [InlineData("Status", "unfilled")]
    public void Incomplete_order_details_cannot_verify_a_profile(string field, string? value)
    {
        var bought = JsonNode.Parse(Json(Capture("bought")))!;
        bought["orderDetails"]![0]![field] = field == "RawFilled" ? JsonValue.Create(0m) : JsonValue.Create(value);
        AssertRejected(Json(Capture("before")), bought.ToJsonString(), Json(Capture("sold")));
    }

    [Fact]
    public void Fully_credited_fee_cannot_prove_the_buy_amount_convention()
    {
        var bought = JsonNode.Parse(Json(Capture("bought")))!;
        var sold = JsonNode.Parse(Json(Capture("sold")))!;
        foreach (var capture in new[] { bought, sold })
        {
            var history = capture["orderHistory"]![0]!;
            history["FeeCredit"] = .075m;
            var detail = capture["orderDetails"]![0]!;
            detail["FeeCredit"] = .075m;
            detail["History"]![0]!["FeeCredit"] = .075m;
        }
        SetWallet(bought, "BTC", .00001m);
        // Keep a consistent sale and wallet so rejection concerns the ambiguous buy fee.
        SetSale(sold, .00001m, 3_000_000m, .075m);
        SetWallet(sold, "THB", 99.925m);
        var error = Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(Encoding(Json(Capture("before"))),
            Encoding(bought.ToJsonString()), Encoding(sold.ToJsonString()), Fingerprint, Now));
        Assert.Equal("BITKUB_BUY_CASH_FEE_NOT_PROVEN", error.Message);
    }

    [Fact]
    public void Exact_received_quantity_keeps_exact_mode_and_hash_binds_original_bytes()
    {
        var before = Json(Capture("before"));
        var bought = JsonNode.Parse(Json(Capture("bought")))!;
        var sold = JsonNode.Parse(Json(Capture("sold")))!;
        foreach (var capture in new[] { bought, sold })
        {
            capture["orderHistory"]![0]!["Rate"] = 2_992_500m;
            var detail = capture["orderDetails"]![0]!;
            detail["Rate"] = 2_992_500m;
            detail["History"]![0]!["Rate"] = 2_992_500m;
        }
        SetWallet(bought, "BTC", .00001m);
        SetSale(sold, .00001m, 2_992_500m, .0748125m);
        SetWallet(sold, "THB", 99.8501875m);
        var profile = BitkubProfileVerifier.Verify(Encoding(before), Encoding(bought.ToJsonString()), Encoding(sold.ToJsonString()), Fingerprint, Now);
        Assert.Equal(BitkubBuyBaseRounding.Exact, profile.Rules.Pairs.Single().BuyBaseRounding);
        var whitespace = BitkubProfileVerifier.Verify(Encoding(before + " "), Encoding(bought.ToJsonString()), Encoding(sold.ToJsonString()), Fingerprint, Now);
        Assert.NotEqual(profile.EvidenceHash, whitespace.EvidenceHash);
    }

    private static void AssertRejected(string before, string bought, string sold)
        => Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(Encoding(before), Encoding(bought), Encoding(sold), Fingerprint, Now));
    private static JsonNode MarketCapture(string stage)
    {
        var capture = JsonNode.Parse(Json(Capture(stage)))!;
        foreach (var history in capture["orderHistory"]!.AsArray()) history!["Type"] = "market";
        foreach (var detail in capture["orderDetails"]!.AsArray()) detail!["Rate"] = 0m;
        return capture;
    }
    private static void SplitBuyFill(JsonNode capture)
    {
        var history = capture["orderHistory"]!.AsArray();
        var secondHistory = history[0]!.DeepClone();
        history[0]!["RawAmount"] = 10m; history[0]!["ReportedFee"] = .025m;
        secondHistory["TransactionId"] = "buy-tx-2";
        secondHistory["RawAmount"] = 20m; secondHistory["ReportedFee"] = .05m;
        history.Insert(1, secondHistory);
        capture["historyCounts"]!["BTC-THB"] = history.Count;
        var fills = capture["orderDetails"]![0]!["History"]!.AsArray();
        var secondFill = fills[0]!.DeepClone();
        fills[0]!["RawAmount"] = 10m; fills[0]!["ReportedFee"] = .025m;
        secondFill["TransactionId"] = "buy-tx-2";
        secondFill["RawAmount"] = 20m; secondFill["ReportedFee"] = .05m;
        fills.Add(secondFill);
    }
    private static void SetObservedAt(JsonNode capture, DateTimeOffset observedAt)
    {
        capture["observedAt"] = JsonValue.Create(observedAt);
        foreach (var rule in capture["marketRules"]!.AsArray()) rule!["ObservedAt"] = JsonValue.Create(observedAt.AddSeconds(-1));
    }
    private static void SetWallet(JsonNode capture, string currency, decimal amount)
    {
        var balance = capture["walletBalances"]!.AsArray().Single(b => b!["Currency"]!.GetValue<string>() == currency)!;
        balance["Available"] = amount; balance["Total"] = amount;
        if (currency == "THB") { capture["thbAvailable"] = amount; capture["thbTotal"] = amount; }
    }
    private static void SetSale(JsonNode sold, decimal quantity, decimal rate, decimal fee)
    {
        var history = sold["orderHistory"]![1]!;
        history["RawAmount"] = quantity; history["Rate"] = rate; history["ReportedFee"] = fee;
        var detail = sold["orderDetails"]![1]!;
        foreach (var field in new[] { "RawAmount", "RawTotal", "RawFilled" }) detail[field] = quantity;
        detail["Rate"] = rate; detail["ReportedFee"] = fee;
        var raw = detail["History"]![0]!;
        raw["RawAmount"] = quantity; raw["Rate"] = rate; raw["ReportedFee"] = fee;
    }

    private static BitkubVerifiedProfile Verify()
        => BitkubProfileVerifier.Verify(Encoding(Json(Capture("before"))), Encoding(Json(Capture("bought"))), Encoding(Json(Capture("sold"))), Fingerprint, Now);

    private static Dictionary<string, object?> Capture(string stage)
    {
        var observed = stage == "before" ? Now.AddMinutes(-2) : stage == "bought" ? Now.AddMinutes(-1) : Now;
        var btc = stage == "before" || stage == "sold" ? 0m : .00000997m;
        var thb = stage == "before" ? 100m : stage == "bought" ? 70m : 99.835225m;
        var rules = new[] { Symbol("BTC-THB", observed), Symbol("ETH-THB", observed), Symbol("SOL-THB", observed) };
        var history = new List<object>();
        var details = new List<object>();
        var buyAt = Now.AddMinutes(-1).AddSeconds(-2);
        if (stage != "before") { history.Add(History("buy-tx", "buy-order", "buy", 3_000_000m, 30m, .075m, buyAt)); details.Add(Order(OrderSide.Buy, "buy-order", "buy-order", "buy-tx", 30m, 3_000_000m, .075m, buyAt)); }
        if (stage == "sold") { history.Add(History("sell-tx", "sell-order", "sell", 3_000_000m, .00000997m, .074775m, Now.AddSeconds(-1))); details.Add(Order(OrderSide.Sell, "sell-order", "sell-order", "sell-tx", .00000997m, 3_000_000m, .074775m, Now.AddSeconds(-1))); }
        return new() { ["observedAt"] = observed, ["completed"] = true, ["source"] = "https://api.bitkub.com/api/v4/wallet/balances", ["walletReadSucceeded"] = true, ["liveEnabled"] = false, ["thbAvailable"] = thb, ["thbReserved"] = 0m, ["thbTotal"] = thb, ["nonzeroCoins"] = btc == 0 ? Array.Empty<string>() : new[] { "BTC" }, ["tradePermissionConfirmed"] = true, ["openOrderCount"] = 0, ["historyCounts"] = new Dictionary<string, int> { ["BTC-THB"] = history.Count, ["ETH-THB"] = 0, ["SOL-THB"] = 0 }, ["stableWallet"] = true, ["marketRules"] = rules, ["initialInventoryEmpty"] = true, ["funded"] = true, ["credentialFingerprint"] = Fingerprint, ["walletBalances"] = new[] { new Dictionary<string, object?> { ["Currency"] = "THB", ["Available"] = thb, ["Reserved"] = 0m, ["Total"] = thb }, new Dictionary<string, object?> { ["Currency"] = "BTC", ["Available"] = btc, ["Reserved"] = 0m, ["Total"] = btc } }.Where(x => (decimal)x["Total"]! != 0).ToArray(), ["orderHistory"] = history, ["orderDetails"] = details };
    }

    private static object Symbol(string symbol, DateTimeOffset at) => new Dictionary<string, object?> { ["Symbol"] = symbol, ["ObservedAt"] = at.AddSeconds(-1), ["BaseAssetScale"] = 8, ["QuoteAssetScale"] = 2, ["BaseQuantityScale"] = 8, ["QuoteQuantityScale"] = 2, ["PriceScale"] = 2, ["PriceStep"] = .01m, ["QuantityScale"] = 0, ["QuantityStep"] = 1m, ["MinimumQuoteSize"] = 10m, ["FreezeBuy"] = false, ["FreezeSell"] = false, ["FreezeCancel"] = false, ["Status"] = "active", ["Source"] = "exchange", ["MarketSegment"] = "SPOT" };
    private static object History(string tx, string order, string side, decimal rate, decimal amount, decimal fee, DateTimeOffset at) => new Dictionary<string, object?> { ["Symbol"] = "BTC-THB", ["TransactionId"] = tx, ["OrderId"] = order, ["ParentOrderId"] = order, ["SuperOrderId"] = order, ["ClientId"] = order, ["Side"] = side, ["Type"] = "limit", ["Rate"] = rate, ["RawAmount"] = amount, ["ReportedFee"] = fee, ["FeeCredit"] = 0m, ["Timestamp"] = at.ToUnixTimeMilliseconds() };
    private static object Order(OrderSide side, string id, string client, string tx, decimal amount, decimal rate, decimal fee, DateTimeOffset at) => new Dictionary<string, object?> { ["Symbol"] = "BTC-THB", ["Side"] = side, ["OrderId"] = id, ["FirstOrderId"] = id, ["ParentOrderId"] = id, ["LastOrderId"] = id, ["ClientId"] = client, ["PostOnly"] = false, ["RawAmount"] = amount, ["Rate"] = rate, ["ReportedFee"] = fee, ["FeeCredit"] = 0m, ["RawFilled"] = amount, ["RawTotal"] = amount, ["Status"] = "filled", ["PartiallyFilled"] = false, ["RawRemaining"] = 0m, ["History"] = new[] { new Dictionary<string, object?> { ["OrderId"] = id, ["TransactionId"] = tx, ["Rate"] = rate, ["RawAmount"] = amount, ["ReportedFee"] = fee, ["FeeCredit"] = 0m, ["Timestamp"] = at.ToUnixTimeMilliseconds() } } };
    private static string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    private static ReadOnlyMemory<byte> Encoding(string value) => System.Text.Encoding.UTF8.GetBytes(value);
}
