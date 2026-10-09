using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubDynamicMarketTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("XRP-THB")]
    [InlineData("DOGE-THB")]
    [InlineData("USDT-THB")]
    [InlineData("1INCH-THB")]
    public void Canonical_nonlegacy_THB_markets_are_valid(string symbol)
        => Assert.True(LiveMarketSymbol.IsValid(symbol));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xrp-THB")]
    [InlineData("XRP_THB")]
    [InlineData("XRP-USD")]
    [InlineData("THB-THB")]
    [InlineData(" XRP-THB")]
    [InlineData("XRP-THB\n")]
    [InlineData("../withdraw-THB")]
    [InlineData("XRP-THB?account=other")]
    public void Noncanonical_or_injected_symbols_are_invalid(string? symbol)
        => Assert.False(LiveMarketSymbol.IsValid(symbol));

    [Fact]
    public async Task Public_discovery_includes_fourth_and_fifth_markets_with_distinct_precision()
    {
        var wire = new Wire(Envelope(Markets().Select(MarketItem)));
        using var broker = Broker(wire);
        var markets = await broker.GetAvailableMarketsAsync(default);
        Assert.Equal(new[] { "BTC-THB", "DOGE-THB", "ETH-THB", "SOL-THB", "USDT-THB", "XRP-THB" },
            markets.Select(m => m.Symbol).Order(StringComparer.Ordinal));
        Assert.Equal(.00001m, markets.Single(m => m.Symbol == "XRP-THB").PriceStep);
        Assert.Equal(.0000001m, markets.Single(m => m.Symbol == "DOGE-THB").PriceStep);
        Assert.Equal(2, markets.Single(m => m.Symbol == "USDT-THB").BaseQuantityScale);
        Assert.All(markets, m => Assert.Equal(Now, m.ObservedAt));
        Assert.Equal(new[] { "/api/v3/market/symbols" }, wire.Paths);
        Assert.False(wire.Credentials);
    }

    [Theory]
    [InlineData("delisted")]
    [InlineData("frozen-buy")]
    [InlineData("frozen-sell")]
    [InlineData("frozen-cancel")]
    [InlineData("broker-source")]
    [InlineData("other-segment")]
    [InlineData("other-quote")]
    public async Task Unavailable_or_nonexchange_markets_are_excluded(string defect)
    {
        var item = MarketItem(Market("DOGE-THB", priceScale: 7));
        switch (defect)
        {
            case "delisted": item["status"] = "delisted"; break;
            case "frozen-buy": item["freeze_buy"] = true; break;
            case "frozen-sell": item["freeze_sell"] = true; break;
            case "frozen-cancel": item["freeze_cancel"] = true; break;
            case "broker-source": item["source"] = "broker"; break;
            case "other-segment": item["market_segment"] = "MARGIN"; break;
            case "other-quote": item["quote_asset"] = "USD"; item["symbol"] = "DOGE_USD"; break;
        }
        using var broker = Broker(new Wire(Envelope([MarketItem(Market("XRP-THB", priceScale: 5)), item])));
        Assert.Equal("XRP-THB", Assert.Single(await broker.GetAvailableMarketsAsync(default)).Symbol);
    }

    [Theory]
    [InlineData("base-mismatch")]
    [InlineData("missing-scale")]
    [InlineData("price-step")]
    [InlineData("quantity-step")]
    [InlineData("injection")]
    [InlineData("duplicate")]
    public async Task Active_malformed_or_duplicate_metadata_fails_closed(string defect)
    {
        var item = MarketItem(Market("XRP-THB", priceScale: 5));
        switch (defect)
        {
            case "base-mismatch": item["base_asset"] = "DOGE"; break;
            case "missing-scale": item.Remove("base_quantity_scale"); break;
            case "price-step": item["price_step"] = "0.01"; break;
            case "quantity-step": item["quantity_step"] = "0.01"; break;
            case "injection": item["symbol"] = "XRP_THB?account=other"; break;
        }
        var items = defect == "duplicate" ? new[] { item, (JsonObject)item.DeepClone() } : [item];
        using var broker = Broker(new Wire(Envelope(items)));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetAvailableMarketsAsync(default));
    }

    [Fact]
    public async Task Oversized_discovery_payload_is_rejected()
    {
        Assert.Equal(1000, LiveMarketSymbol.MaximumPayloadMarkets);
        var items = Enumerable.Range(0, LiveMarketSymbol.MaximumPayloadMarkets + 1)
            .Select(i => MarketItem(Market($"TEST{i}-THB")));
        using var broker = Broker(new Wire(Envelope(items)));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetAvailableMarketsAsync(default));
    }

    [Fact]
    public async Task Bulk_quotes_use_one_public_read_and_preserve_requested_precision()
    {
        var wire = new Wire(JsonSerializer.Serialize(new[]
        {
            Quote("BTC_THB", "100000", "100001"), Quote("XRP_THB", "2.12345", "2.12346"),
            Quote("DOGE_THB", "0.1234567", "0.1234568"), Quote("USDT_THB", "33.12", "33.13")
        }));
        using var broker = Broker(wire);
        var quotes = await broker.GetQuotesAsync(["XRP-THB", "DOGE-THB", "USDT-THB"], default);
        Assert.Equal(3, quotes.Count);
        Assert.Equal(new LiveQuote("XRP-THB", 2.12345m, 2.12346m, Now), quotes.Single(q => q.Symbol == "XRP-THB"));
        Assert.Equal(new LiveQuote("DOGE-THB", .1234567m, .1234568m, Now), quotes.Single(q => q.Symbol == "DOGE-THB"));
        Assert.Equal(new LiveQuote("USDT-THB", 33.12m, 33.13m, Now), quotes.Single(q => q.Symbol == "USDT-THB"));
        Assert.Equal(new[] { "/api/v3/market/ticker" }, wire.Paths);
        Assert.False(wire.Credentials);
    }

    [Theory]
    [InlineData("../withdraw")]
    [InlineData("XRP-THB?account=other")]
    [InlineData("XRP_USD")]
    public async Task Injected_quote_request_never_reaches_transport(string symbol)
    {
        var wire = new Wire("[]");
        using var broker = Broker(wire);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetQuotesAsync([symbol], default));
        Assert.Empty(wire.Paths);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("crossed")]
    [InlineData("nonnumeric")]
    public async Task Unusable_requested_bulk_quote_fails_closed(string defect)
    {
        var quote = Quote("XRP_THB", defect == "nonnumeric" ? "not-a-price" : "2.12345", defect == "crossed" ? "2" : "2.12346");
        var quotes = defect == "duplicate" ? new[] { quote, quote } : [quote];
        using var broker = Broker(new Wire(JsonSerializer.Serialize(quotes)));
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetQuotesAsync(["XRP-THB"], default));
    }

    [Fact]
    public async Task Missing_or_zero_books_are_omitted_without_fabricating_prices()
    {
        var wire = new Wire(JsonSerializer.Serialize(new[]
        {
            Quote("XRP_THB", "2.12345", "2.12346"), Quote("DOGE_THB", "0", "0.1"),
            Quote("USDT_THB", "33", "0")
        }));
        using var broker = Broker(wire);
        var quotes = await broker.GetQuotesAsync(["XRP-THB", "DOGE-THB", "USDT-THB", "SOL-THB"], default);
        Assert.Equal(new LiveQuote("XRP-THB", 2.12345m, 2.12346m, Now), Assert.Single(quotes));
        Assert.Equal(new[] { "/api/v3/market/ticker" }, wire.Paths);
    }

    [Fact]
    public async Task Duplicate_requested_symbols_are_rejected_before_transport()
    {
        var wire = new Wire("[]");
        using var broker = Broker(wire);
        await Assert.ThrowsAsync<InvalidDataException>(() => broker.GetQuotesAsync(["XRP-THB", "XRP-THB"], default));
        Assert.Empty(wire.Paths);
    }

    [Fact]
    public void Receipt_expansion_preserves_account_fee_and_existing_rules_for_new_precision()
    {
        var previous = PreviousProfile();
        var expanded = BitkubProfileVerifier.ExpandReceiptMarkets(previous, Markets(), Fingerprint, Now);
        Assert.Equal(6, expanded.Rules.Pairs.Count);
        Assert.Equal(previous.BrokerAccountReference, expanded.BrokerAccountReference);
        Assert.Equal(previous.CredentialFingerprint, expanded.CredentialFingerprint);
        Assert.Equal(previous.AccountBindingVerified, expanded.AccountBindingVerified);
        Assert.Equal(previous.TradePermissionVerified, expanded.TradePermissionVerified);
        Assert.Equal(previous.Rules.MaximumCashFeeRate, expanded.Rules.MaximumCashFeeRate);
        Assert.Equal(previous.Rules.BuyHistoryAmount, expanded.Rules.BuyHistoryAmount);
        Assert.Contains(previous.Rules.Pairs.Single(), expanded.Rules.Pairs);
        Assert.NotEqual(previous.EvidenceHash, expanded.EvidenceHash);
        Assert.Equal(expanded.EvidenceHash, expanded.Rules.EvidenceHash);
        foreach (var market in Markets())
        {
            var pair = expanded.Rules.Pairs.Single(p => p.Symbol == market.Symbol);
            Assert.Equal(1m, pair.BuyQuoteStep);
            Assert.Equal(Quantum(market.BaseQuantityScale!.Value), pair.SellBaseStep);
            Assert.Equal(market.PriceStep, pair.PriceStep);
            Assert.Equal(market.QuantityScale, pair.RawQuantityScale);
            Assert.Equal(market.QuantityStep, pair.RawQuantityStep);
            Assert.Equal(BitkubBuyBaseRounding.FloorToBaseStep, pair.BuyBaseRounding);
        }
        Assert.Same(expanded, BitkubProfileVerifier.ExpandReceiptMarkets(expanded, Markets(), Fingerprint, Now));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("frozen")]
    [InlineData("source")]
    [InlineData("segment")]
    [InlineData("symbol")]
    [InlineData("base-scale")]
    [InlineData("base-quantity-scale")]
    [InlineData("quote-scale")]
    [InlineData("quote-quantity-scale")]
    [InlineData("price-step")]
    [InlineData("quantity-step")]
    [InlineData("minimum")]
    public void Receipt_market_validation_rejects_unproven_or_inconsistent_metadata(string defect)
    {
        var valid = Market("XRP-THB", priceScale: 5);
        Assert.True(BitkubProfileVerifier.IsReceiptMarket(valid, Now));
        var changed = defect switch
        {
            "stale" => valid with { ObservedAt = Now.AddSeconds(-61) },
            "future" => valid with { ObservedAt = Now.AddTicks(1) },
            "frozen" => valid with { FreezeSell = true },
            "source" => valid with { Source = "broker" },
            "segment" => valid with { MarketSegment = "MARGIN" },
            "symbol" => valid with { Symbol = "XRP-USD" },
            "base-scale" => valid with { BaseAssetScale = 29 },
            "base-quantity-scale" => valid with { BaseQuantityScale = 9 },
            "quote-scale" => valid with { QuoteAssetScale = 8 },
            "quote-quantity-scale" => valid with { QuoteQuantityScale = 8 },
            "price-step" => valid with { PriceStep = .01m },
            "quantity-step" => valid with { QuantityStep = .01m },
            _ => valid with { MinimumQuoteSize = 9m }
        };
        Assert.False(BitkubProfileVerifier.IsReceiptMarket(changed, Now));
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.ExpandReceiptMarkets(
            PreviousProfile(), [Market("BTC-THB"), changed], Fingerprint, Now));
    }

    [Theory]
    [InlineData("missing-old")]
    [InlineData("changed-old")]
    [InlineData("duplicate")]
    [InlineData("binding")]
    [InlineData("fee-mode")]
    [InlineData("evidence")]
    public void Expansion_cannot_rewrite_old_rules_or_bypass_existing_profile_binding(string defect)
    {
        var previous = PreviousProfile();
        var markets = Markets().ToList();
        switch (defect)
        {
            case "missing-old": markets.RemoveAt(0); break;
            case "changed-old": markets[0] = Market("BTC-THB", priceScale: 3); break;
            case "duplicate": markets.Add(markets[^1]); break;
            case "binding": previous = previous with { BrokerAccountReference = "another-account" }; break;
            case "fee-mode": previous = previous with { Rules = previous.Rules with { BuyHistoryAmount = BitkubBuyHistoryAmount.QuoteIncludingCashFee } }; break;
            case "evidence": previous = previous with { EvidenceHash = new string('b', 64) }; break;
        }
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.ExpandReceiptMarkets(previous, markets, Fingerprint, Now));
    }

    [Fact]
    public void Receipt_profile_accepts_verified_nonlegacy_coin_inventory_without_increasing_authority()
    {
        var profile = BitkubProfileVerifier.ForOrderReceipts(ReceiptCapture(), Fingerprint, Now);
        Assert.Equal(6, profile.Rules.Pairs.Count);
        Assert.Equal("bitkub:credential:" + Fingerprint[7..], profile.BrokerAccountReference);
        Assert.Equal(BitkubBuyHistoryAmount.AuthenticatedOrderReceipts, profile.Rules.BuyHistoryAmount);
    }

    [Fact]
    public void Three_historical_nonlegacy_orders_do_not_block_a_receipt_profile_or_change_binding()
    {
        var markets = Markets().Where(m => m.Symbol is "XRP-THB" or "DOGE-THB" or "USDT-THB").ToArray();
        var history = markets.Select((m, i) => History(m.Symbol, i)).ToArray();
        var orders = markets.Select((m, i) => HistoricalOrder(m.Symbol, i)).ToArray();
        var capture = ReceiptCapture(markets, history, orders);
        var profile = BitkubProfileVerifier.ForOrderReceipts(capture, Fingerprint, Now);
        Assert.Equal(markets.Select(m => m.Symbol).Order(StringComparer.Ordinal), profile.Rules.Pairs.Select(p => p.Symbol));
        Assert.Equal("bitkub:credential:" + Fingerprint[7..], profile.BrokerAccountReference);
        Assert.Equal(Fingerprint, profile.CredentialFingerprint);
        Assert.True(profile.AccountBindingVerified);
        Assert.True(profile.TradePermissionVerified);
        Assert.Equal(.0035m, profile.Rules.MaximumCashFeeRate);
        Assert.Equal(BitkubBuyHistoryAmount.AuthenticatedOrderReceipts, profile.Rules.BuyHistoryAmount);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(capture)), profile.EvidenceHash);
        Assert.Equal(profile.EvidenceHash, profile.Rules.EvidenceHash);
    }

    [Theory]
    [InlineData("history", 1000, true)]
    [InlineData("history", 1001, false)]
    [InlineData("details", 1000, true)]
    [InlineData("details", 1001, false)]
    public void Receipt_history_and_detail_counts_are_bounded_without_the_canary_two_order_limit(string field, int count, bool accepted)
    {
        var history = field == "history" ? Enumerable.Range(0, count).Select(i => History("XRP-THB", i)).ToArray() : [];
        var details = field == "details" ? Enumerable.Range(0, count).Select(i => HistoricalOrder("XRP-THB", i, filled: false)).ToArray() : [];
        var capture = ReceiptCapture(history: history, details: details);
        Assert.InRange(capture.Length, 1, 512 * 1024);
        if (accepted)
            Assert.Equal(Fingerprint, BitkubProfileVerifier.ForOrderReceipts(capture, Fingerprint, Now).CredentialFingerprint);
        else
            Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.ForOrderReceipts(capture, Fingerprint, Now));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Receipt_capture_byte_bound_accepts_512_KiB_and_rejects_the_next_byte(int extraBytes, bool accepted)
    {
        var json = ReceiptCapture();
        var padded = new byte[512 * 1024 + extraBytes];
        Array.Fill(padded, (byte)' ');
        json.CopyTo(padded, 0);
        if (accepted)
            Assert.Equal(Fingerprint, BitkubProfileVerifier.ForOrderReceipts(padded, Fingerprint, Now).CredentialFingerprint);
        else
            Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.ForOrderReceipts(padded, Fingerprint, Now));
    }

    [Fact]
    public void Historical_canary_keeps_its_two_order_contract_after_receipt_mode_expansion()
    {
        var markets = Markets().Take(3).ToArray();
        var buy = History("BTC-THB", 0) with { TransactionId = "buy-tx", OrderId = "buy-order", ParentOrderId = "buy-order",
            SuperOrderId = "buy-order", ClientId = "buy-client", Rate = 3_000_000m, RawAmount = 30, ReportedFee = .075m,
            Timestamp = Now.AddMinutes(-1).ToUnixTimeMilliseconds() };
        var sale = History("BTC-THB", 1) with { TransactionId = "sell-tx", OrderId = "sell-order", ParentOrderId = "sell-order",
            SuperOrderId = "sell-order", ClientId = "sell-client", Side = "sell", Rate = 3_000_000m,
            RawAmount = .00000997m, ReportedFee = .074775m };
        var buyOrder = HistoricalOrder("BTC-THB", 0) with { OrderId = buy.OrderId, FirstOrderId = buy.OrderId,
            ParentOrderId = buy.OrderId, LastOrderId = buy.OrderId, ClientId = buy.ClientId, RawAmount = buy.RawAmount,
            Rate = buy.Rate, ReportedFee = buy.ReportedFee, RawFilled = buy.RawAmount, RawTotal = buy.RawAmount,
            History = [new(buy.OrderId, buy.TransactionId, buy.Rate, buy.RawAmount, buy.ReportedFee, 0, buy.Timestamp)] };
        var sellOrder = HistoricalOrder("BTC-THB", 1) with { Side = OrderSide.Sell, OrderId = sale.OrderId, FirstOrderId = sale.OrderId,
            ParentOrderId = sale.OrderId, LastOrderId = sale.OrderId, ClientId = sale.ClientId, RawAmount = sale.RawAmount,
            Rate = sale.Rate, ReportedFee = sale.ReportedFee, RawFilled = sale.RawAmount, RawTotal = sale.RawAmount,
            History = [new(sale.OrderId, sale.TransactionId, sale.Rate, sale.RawAmount, sale.ReportedFee, 0, sale.Timestamp)] };
        var before = ReceiptCapture(markets, wallet: [new("THB", 100, 0, 100), new("BTC", 0, 0, 0)], observed: Now.AddMinutes(-2));
        var bought = ReceiptCapture(markets, [buy], [buyOrder],
            [new("THB", 70, 0, 70), new("BTC", .00000997m, 0, .00000997m)], Now.AddMinutes(-1));
        var sold = ReceiptCapture(markets, [buy, sale], [buyOrder, sellOrder],
            [new("THB", 99.835225m, 0, 99.835225m), new("BTC", 0, 0, 0)]);
        Assert.True(BitkubProfileVerifier.Verify(before, bought, sold, Fingerprint, Now).AccountBindingVerified);
        var threeOrders = JsonNode.Parse(sold)!.AsObject();
        threeOrders["orderDetails"]!.AsArray().Add(JsonSerializer.SerializeToNode(HistoricalOrder("BTC-THB", 2, filled: false)));
        Assert.Throws<InvalidDataException>(() => BitkubProfileVerifier.Verify(before, bought,
            Encoding.UTF8.GetBytes(threeOrders.ToJsonString()), Fingerprint, Now));
    }

    [Fact]
    public void Risk_accepts_nonlegacy_positions_and_pending_markets_under_owner_limits()
    {
        var mandate = LiveRiskTests.Mandate with { AllowedSymbols = ["XRP-THB", "DOGE-THB", "USDT-THB"] };
        var snapshot = LiveRiskTests.Snapshot with
        {
            ObservedAt = Now, CashBalance = 990, CashReserved = 1, PendingBuyNotional = 1,
            Positions = [new("DOGE-THB", 10, 0, 1)], PendingBuySymbols = ["USDT-THB"],
            Quotes = [new("XRP-THB", 1.99999m, 2m, Now)]
        };
        var intent = LiveRiskTests.Intent with { Symbol = "XRP-THB", Quantity = 100, LimitPrice = 2 };
        var decision = LiveRiskPolicy.Evaluate(intent, snapshot, mandate, LiveRiskTests.Gate, Now);
        Assert.True(decision.Allowed, string.Join(",", decision.Violations));
        Assert.Equal(990m, snapshot.CashBalance);
    }

    [Theory]
    [InlineData("allowlist", "SYMBOL_NOT_ALLOWED")]
    [InlineData("limits", "INVALID_MANDATE")]
    [InlineData("quote", "QUOTE_UNAVAILABLE")]
    [InlineData("stale-quote", "QUOTE_UNAVAILABLE")]
    [InlineData("disarmed", "NOT_ARMED")]
    [InlineData("reconciliation", "RECONCILIATION_REQUIRED")]
    public void New_market_support_retains_owner_quote_and_execution_gates(string defect, string violation)
    {
        var mandate = LiveRiskTests.Mandate with { AllowedSymbols = ["XRP-THB"] };
        var snapshot = LiveRiskTests.Snapshot with { ObservedAt = Now, Quotes = [new("XRP-THB", 1.99999m, 2, Now)] };
        var gate = LiveRiskTests.Gate;
        switch (defect)
        {
            case "allowlist": mandate = mandate with { AllowedSymbols = ["BTC-THB"] }; break;
            case "limits": mandate = mandate with { MaximumOrderNotional = 0 }; break;
            case "quote": snapshot = snapshot with { Quotes = [] }; break;
            case "stale-quote": snapshot = snapshot with { Quotes = [new("XRP-THB", 1.99999m, 2, Now.AddSeconds(-31))] }; break;
            case "disarmed": gate = gate with { Armed = false }; break;
            case "reconciliation": snapshot = snapshot with { Reconciled = false }; break;
        }
        var intent = LiveRiskTests.Intent with { Symbol = "XRP-THB", Quantity = 100, LimitPrice = 2 };
        var result = LiveRiskPolicy.Evaluate(intent, snapshot, mandate, gate, Now);
        Assert.False(result.Allowed);
        Assert.Contains(violation, result.Violations);
    }

    private static BrokerSymbolRules[] Markets() =>
    [
        Market("BTC-THB"), Market("ETH-THB"), Market("SOL-THB"),
        Market("XRP-THB", priceScale: 5, quantityScale: 2), Market("DOGE-THB", priceScale: 7),
        Market("USDT-THB", baseScale: 2, baseQuantityScale: 2)
    ];
    private static BrokerSymbolRules Market(string symbol, int baseScale = 8, int baseQuantityScale = 8,
        int priceScale = 2, int quantityScale = 0) => new(symbol, Now, baseScale, 2, baseQuantityScale, 2,
            priceScale, Quantum(priceScale), quantityScale, Quantum(quantityScale), 10m,
            false, false, false, "active", "exchange", "SPOT");
    private static decimal Quantum(int scale) { var step = 1m; for (var i = 0; i < scale; i++) step /= 10m; return step; }
    private static BrokerHistoryEntry History(string symbol, int index) => new(symbol, "t" + index, "o" + index,
        "o" + index, "o" + index, "manual", "buy", "limit", 2, 10, .025m, 0, Now.ToUnixTimeMilliseconds());
    private static BrokerOrderEvidence HistoricalOrder(string symbol, int index, bool filled = true) => new(symbol, OrderSide.Buy,
        "o" + index, "o" + index, "o" + index, "o" + index, "manual", false, 10, 2, filled ? .025m : 0, 0,
        filled ? 10 : 0, 10, filled ? "filled" : "cancelled", false, 0,
        filled ? [new("o" + index, "t" + index, 2, 10, .025m, 0, Now.ToUnixTimeMilliseconds())] : []);
    private static byte[] ReceiptCapture(BrokerSymbolRules[]? markets = null, BrokerHistoryEntry[]? history = null,
        BrokerOrderEvidence[]? details = null, BrokerWalletBalance[]? wallet = null, DateTimeOffset? observed = null)
    {
        markets ??= Markets(); history ??= []; details ??= [];
        wallet ??= [new("THB", 0, 0, 0), new("XRP", 20, 0, 20), new("DOGE", 100, 0, 100), new("USDT", 2.12m, 0, 2.12m)];
        var at = observed ?? Now;
        var cash = wallet.Single(b => b.Currency == "THB");
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            observedAt = at, completed = true, source = "https://api.bitkub.com/api/v4/wallet/balances",
            walletReadSucceeded = true, liveEnabled = false, thbAvailable = cash.Available, thbReserved = cash.Reserved, thbTotal = cash.Total,
            nonzeroCoins = wallet.Where(b => b.Currency != "THB" && b.Total != 0).Select(b => b.Currency).ToArray(),
            tradePermissionConfirmed = true, openOrderCount = 0, historyCounts = markets.ToDictionary(m => m.Symbol, m => history.Count(h => h.Symbol == m.Symbol)),
            stableWallet = true, marketRules = markets.Select(m => m with { ObservedAt = at }).ToArray(),
            initialInventoryEmpty = wallet.All(b => b.Currency == "THB" || b.Total == 0), funded = wallet.Any(b => b.Total > 0),
            credentialFingerprint = Fingerprint, walletBalances = wallet, orderHistory = history, orderDetails = details
        });
    }
    private static BitkubVerifiedProfile PreviousProfile() => new("bitkub:credential:" + Fingerprint[7..],
        Fingerprint, "old", new string('a', 64), true, true,
        new("old;https://www.bitkub.com/en/fee/cryptocurrency", new string('a', 64), .0035m,
            BitkubBuyHistoryAmount.AuthenticatedOrderReceipts,
            [new("BTC-THB", 1, .00000001m, .01m, 10, 0, 1) { BuyBaseRounding = BitkubBuyBaseRounding.FloorToBaseStep }]));
    private static JsonObject MarketItem(BrokerSymbolRules m) => new()
    {
        ["symbol"] = m.Symbol.Replace('-', '_'), ["base_asset"] = m.Symbol[..^4], ["quote_asset"] = "THB",
        ["base_asset_scale"] = m.BaseAssetScale, ["quote_asset_scale"] = m.QuoteAssetScale,
        ["base_quantity_scale"] = m.BaseQuantityScale, ["quote_quantity_scale"] = m.QuoteQuantityScale,
        ["price_scale"] = m.PriceScale, ["price_step"] = m.PriceStep.ToString(CultureInfo.InvariantCulture),
        ["quantity_scale"] = m.QuantityScale, ["quantity_step"] = m.QuantityStep.ToString(CultureInfo.InvariantCulture),
        ["min_quote_size"] = m.MinimumQuoteSize, ["freeze_buy"] = m.FreezeBuy, ["freeze_sell"] = m.FreezeSell,
        ["freeze_cancel"] = m.FreezeCancel, ["status"] = m.Status, ["source"] = m.Source, ["market_segment"] = m.MarketSegment
    };
    private static string Envelope(IEnumerable<JsonObject> items) => new JsonObject
        { ["error"] = 0, ["result"] = new JsonArray(items.Select(i => (JsonNode)i).ToArray()) }.ToJsonString();
    private static object Quote(string symbol, string bid, string ask) => new { symbol, highest_bid = bid, lowest_ask = ask };
    private static BitkubLiveBroker Broker(Wire wire) => new("test-key", "test-secret", wire, new Clock());
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Wire(string payload) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public bool Credentials { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Paths.Add(request.RequestUri!.PathAndQuery);
            Credentials |= request.Headers.Any(h => h.Key.StartsWith("X-BTK-", StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        }
    }
}
