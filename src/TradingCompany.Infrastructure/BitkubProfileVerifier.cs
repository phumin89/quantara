using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.Infrastructure;

// Same JSON shape as the executor's BitkubLiveVerification. This has no order authority.
public sealed record BitkubVerifiedProfile(string BrokerAccountReference, string CredentialFingerprint,
    string ConfirmationReference, string EvidenceHash, bool AccountBindingVerified, bool TradePermissionVerified,
    BitkubVerifiedExecutionRules Rules);

public static class BitkubProfileVerifier
{
    // Published THB Exchange fee plus a conservative one-cent rounding allowance at
    // the published 10 THB minimum. This is a cash ceiling, not a new broker fee.
    private const decimal CashFeeCeiling = .0035m;
    private const string FeeReference = "https://www.bitkub.com/en/fee/cryptocurrency";
    private static readonly JsonSerializerOptions Json = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    // No manual sale or global cash-fee convention is required. Every future fill
    // requires a durable authenticated receipt with explicit wallet debit/receive.
    public static BitkubVerifiedProfile ForOrderReceipts(ReadOnlyMemory<byte> bytes, string fingerprint, DateTimeOffset now)
    {
        try
        {
            if (fingerprint is not { Length: 71 } || !fingerprint.StartsWith("sha256:", StringComparison.Ordinal) ||
                fingerprint[7..].Any(c => !char.IsAsciiHexDigit(c))) throw Invalid();
            var capture = Read(bytes, fingerprint, now, receiptMode: true);
            if (now - capture.observedAt > TimeSpan.FromMinutes(5) ||
                capture.walletBalances.Any(b => b.Currency != "THB" && b.Total != 0 &&
                    !capture.marketRules.Any(m => m.Symbol == b.Currency + "-THB")) ||
                capture.walletBalances.All(b => b.Total == 0)) throw Invalid();
            var pairs = ReceiptPairs(capture.marketRules, now);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
            var reference = "authenticated-bitkub-order-receipts:" + capture.observedAt.ToUnixTimeMilliseconds();
            var rules = new BitkubVerifiedExecutionRules(reference + ";" + FeeReference +
                ";https://github.com/bitkub/bitkub-official-api-docs/blob/master/websocket-private.md", hash, CashFeeCeiling,
                BitkubBuyHistoryAmount.AuthenticatedOrderReceipts,
                pairs);
            return new("bitkub:credential:" + fingerprint[7..], fingerprint, reference, hash, true, true,
                BitkubExecutionAdapter.ValidateRules(rules));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or OverflowException or ArgumentException)
        { throw Invalid(); }
    }

    // Reversible market expansion only; retains the existing key/account and fee model.
    public static BitkubVerifiedProfile ExpandReceiptMarkets(BitkubVerifiedProfile previous,
        IReadOnlyList<BrokerSymbolRules> markets, string fingerprint, DateTimeOffset now)
    {
        if (fingerprint is not { Length: 71 } || !fingerprint.StartsWith("sha256:", StringComparison.Ordinal) ||
            fingerprint[7..].Any(c => !char.IsAsciiHexDigit(c)) ||
            previous.CredentialFingerprint != fingerprint || !previous.AccountBindingVerified || !previous.TradePermissionVerified ||
            previous.BrokerAccountReference != "bitkub:credential:" + fingerprint[7..] ||
            previous.Rules.BuyHistoryAmount != BitkubBuyHistoryAmount.AuthenticatedOrderReceipts ||
            previous.EvidenceHash != previous.Rules.EvidenceHash) throw Invalid();
        _ = BitkubExecutionAdapter.ValidateRules(previous.Rules);
        var pairs = ReceiptPairs(markets, now);
        if (previous.Rules.Pairs.OrderBy(p => p.Symbol).SequenceEqual(pairs)) return previous;
        // Expansion never changes any rule already used by outstanding orders.
        if (previous.Rules.Pairs.Any(p => !pairs.Contains(p))) throw Invalid();
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { previous.EvidenceHash, markets })));
        var reference = "authenticated-bitkub-multi-market:" + now.ToUnixTimeMilliseconds();
        return previous with
        {
            ConfirmationReference = reference, EvidenceHash = hash,
            Rules = previous.Rules with { ConfirmationReference = reference + ";" + FeeReference,
                EvidenceHash = hash, Pairs = pairs }
        };
    }

    private static BitkubPairExecutionRules[] ReceiptPairs(IReadOnlyList<BrokerSymbolRules> markets, DateTimeOffset now)
    {
        var sorted = markets.OrderBy(p => p.Symbol, StringComparer.Ordinal).ToArray();
        if (sorted.Length is 0 or > LiveMarketSymbol.MaximumPayloadMarkets ||
            sorted.Select(p => p.Symbol).Distinct(StringComparer.Ordinal).Count() != sorted.Length ||
            sorted.Any(p => !IsReceiptMarket(p, now))) throw Invalid();
        // Whole-THB buy budgets are conservative across native quantity precision;
        // explicit authenticated wallet receipts still prove every actual debit/fill.
        return sorted.Select(m => new BitkubPairExecutionRules(m.Symbol, 1m, BaseStep(m), m.PriceStep,
            m.MinimumQuoteSize, m.QuantityScale, m.QuantityStep) { BuyBaseRounding = BitkubBuyBaseRounding.FloorToBaseStep }).ToArray();
    }

    public static bool IsReceiptMarket(BrokerSymbolRules? market, DateTimeOffset now) => market is { } m &&
        LiveMarketSymbol.IsValid(m.Symbol) && m.BaseAssetScale is >= 0 and <= 28 && m.QuoteAssetScale == 2 &&
        m.BaseQuantityScale is >= 0 and <= 28 && m.BaseQuantityScale <= m.BaseAssetScale && m.QuoteQuantityScale == 2 &&
        m.PriceScale is >= 0 and <= 28 && m.QuantityScale is >= 0 and <= 28 &&
        m.PriceStep == DecimalStep(m.PriceScale) && m.QuantityStep == DecimalStep(m.QuantityScale) &&
        m.MinimumQuoteSize >= 10 && m.Status == "active" && m.Source == "exchange" && m.MarketSegment == "SPOT" &&
        !m.FreezeBuy && !m.FreezeSell && !m.FreezeCancel && m.ObservedAt <= now && now - m.ObservedAt <= TimeSpan.FromSeconds(60);

    public static decimal BaseStep(BrokerSymbolRules market) => DecimalStep(market.BaseQuantityScale ?? throw Invalid());

    private static decimal DecimalStep(int scale)
    {
        if (scale is < 0 or > 28) throw Invalid();
        decimal step = 1;
        for (var i = 0; i < scale; i++) step /= 10;
        return step;
    }

    public static BitkubVerifiedProfile Verify(ReadOnlyMemory<byte> beforeBytes, ReadOnlyMemory<byte> boughtBytes,
        ReadOnlyMemory<byte> soldBytes, string currentFingerprint, DateTimeOffset now)
    {
        try
        {
            if (currentFingerprint is not { Length: 71 } || !currentFingerprint.StartsWith("sha256:", StringComparison.Ordinal) ||
                currentFingerprint[7..].Any(c => !char.IsAsciiHexDigit(c))) throw Invalid();
            var before = Read(beforeBytes, currentFingerprint, now);
            var bought = Read(boughtBytes, currentFingerprint, now);
            var sold = Read(soldBytes, currentFingerprint, now);
            // Captures are bounded to the broker's history window below. A pause between the
            // owner's actions does not invalidate ordered, corroborated trade evidence.
            if (before.observedAt >= bought.observedAt || bought.observedAt >= sold.observedAt ||
                before.thbTotal <= 0 || sold.thbTotal <= 0 ||
                before.walletBalances.Any(b => b.Currency != "THB" && b.Total != 0) ||
                sold.walletBalances.Any(b => b.Currency != "THB" && b.Total != 0) || before.orderHistory.Length != 0 ||
                before.orderDetails.Length != 0 || bought.orderHistory.Length == 0 || sold.orderHistory.Length <= bought.orderHistory.Length)
                throw Invalid();

            var market = before.marketRules.Single(p => p.Symbol == "BTC-THB");
            if (market.BaseAssetScale != 8 || market.QuoteAssetScale != 2 || market.BaseQuantityScale != 8 ||
                market.QuoteQuantityScale != 2 || market.QuantityScale != 0 || market.QuantityStep != 1 ||
                market.PriceStep != .01m || market.MinimumQuoteSize != 10 ||
                bought.marketRules.Single(p => p.Symbol == market.Symbol) with { ObservedAt = market.ObservedAt } != market ||
                sold.marketRules.Single(p => p.Symbol == market.Symbol) with { ObservedAt = market.ObservedAt } != market)
                throw Invalid();
            var buy = ConfirmOrder(bought, "buy", before.observedAt);
            var saleHistory = sold.orderHistory.Where(h => !bought.orderHistory.Any(b => b.TransactionId == h.TransactionId)).ToArray();
            if (!bought.orderHistory.All(h => sold.orderHistory.Any(s => s == h))) throw Invalid();
            var sale = ConfirmOrder(sold with { orderHistory = saleHistory,
                orderDetails = sold.orderDetails.Where(o => o.Side == OrderSide.Sell).ToArray() }, "sell", bought.observedAt);
            if (buy.OrderId == sale.OrderId || sold.orderDetails.Length != 2 ||
                !sold.orderDetails.Any(o => o == buy || SameOrder(o, buy))) throw Invalid();
            var received = bought.walletBalances.SingleOrDefault(b => b.Currency == "BTC")?.Total ?? 0;
            // Market orders have no limit price; use actual fill bounds only for this
            // read-only proof. This does not authorize market placement or adoption.
            var buyPrice = buy.Rate > 0 ? buy.Rate : buy.History.Max(h => h.Rate);
            var sellPrice = sale.Rate > 0 ? sale.Rate : sale.History.Min(h => h.Rate);
            if (received <= 0 || bought.walletBalances.Any(b => b.Currency is not ("THB" or "BTC") && b.Total != 0) ||
                sale.RawAmount != received || received % .00000001m != 0 || buy.RawAmount % 1m != 0 ||
                buy.RawAmount < market.MinimumQuoteSize || sale.RawAmount * sellPrice < market.MinimumQuoteSize)
                throw Invalid();

            var matches = new List<BitkubVerifiedExecutionRules>();
            foreach (var amount in Enum.GetValues<BitkubBuyHistoryAmount>())
            foreach (var rounding in Enum.GetValues<BitkubBuyBaseRounding>())
            {
                var candidate = new BitkubVerifiedExecutionRules(FeeReference, new string('0', 64), CashFeeCeiling, amount,
                    [new("BTC-THB", 1m, .00000001m, market.PriceStep, market.MinimumQuoteSize, market.QuantityScale, market.QuantityStep)
                        { BuyBaseRounding = rounding }]);
                try
                {
                    var account = new LiveAccountId(Guid.Empty);
                    var buyIntent = new LiveIntent(account, Guid.Empty, Guid.Empty, TradingMode.Live, "BTC-THB", OrderSide.Buy,
                        buy.RawAmount / buyPrice, buyPrice, 1, 1, "canary") { BuyBudget = buy.RawAmount };
                    var buyFills = buy.History.Select(h => BitkubReconciliationSource.Normalize(account, buyIntent, buy.OrderId, h, candidate)).ToArray();
                    var sellIntent = buyIntent with { Side = OrderSide.Sell, Quantity = received, LimitPrice = sellPrice, BuyBudget = null };
                    var sellFills = sale.History.Select(h => BitkubReconciliationSource.Normalize(account, sellIntent, sale.OrderId, h, candidate)).ToArray();
                    if (buyFills.Sum(f => f.Quantity) == received && sellFills.Sum(f => f.Quantity) == received &&
                        before.thbTotal - bought.thbTotal == buyFills.Sum(f => f.GrossQuoteAmount + f.CashFee) &&
                        sold.thbTotal - bought.thbTotal == sellFills.Sum(f => f.GrossQuoteAmount - f.CashFee)) matches.Add(candidate);
                }
                catch (InvalidDataException) { /* Try only the explicitly supported accounting conventions. */ }
            }
            var amountModes = matches.Select(m => m.BuyHistoryAmount).Distinct().ToArray();
            if (amountModes.Length > 1) throw new InvalidDataException("BITKUB_BUY_CASH_FEE_NOT_PROVEN");
            if (amountModes.Length != 1) throw Invalid();
            // Exact evidence does not prove a rounding convention. Keep Exact in that case.
            var selected = matches.FirstOrDefault(m => m.Pairs[0].BuyBaseRounding == BitkubBuyBaseRounding.Exact) ?? matches.Single();
            using var hashInput = new MemoryStream();
            using (var writer = new BinaryWriter(hashInput, Encoding.UTF8, leaveOpen: true))
                foreach (var bytes in new[] { beforeBytes, boughtBytes, soldBytes }) { writer.Write(bytes.Length); writer.Write(bytes.Span); }
            var hash = Convert.ToHexStringLower(SHA256.HashData(hashInput.ToArray()));
            var reference = "authenticated-bitkub-canary:" + sold.observedAt.ToUnixTimeMilliseconds();
            return new("bitkub:credential:" + currentFingerprint[7..], currentFingerprint, reference, hash, true, true,
                BitkubExecutionAdapter.ValidateRules(selected with { ConfirmationReference = reference + ";" + FeeReference, EvidenceHash = hash }));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or OverflowException or ArgumentException)
        { throw Invalid(); }
    }

    private static Capture Read(ReadOnlyMemory<byte> bytes, string fingerprint, DateTimeOffset now, bool receiptMode = false)
    {
        if (bytes.Length is 0 or > 512 * 1024) throw Invalid();
        using var document = JsonDocument.Parse(bytes);
        RejectDuplicates(document.RootElement);
        var capture = JsonSerializer.Deserialize<Capture>(bytes.Span, Json) ?? throw Invalid();
        if (!capture.completed || !capture.walletReadSucceeded || capture.liveEnabled || capture.stableWallet != true ||
            capture.tradePermissionConfirmed != true || capture.openOrderCount != 0 ||
            capture.credentialFingerprint != fingerprint || capture.source != "https://api.bitkub.com/api/v4/wallet/balances" ||
            // Historical canary evidence does not authorize today's money. The command
            // rechecks current key, permission, wallet, orders and market rules before saving.
            // Stay inside the published archive boundary so those trades remain corroborable.
            capture.observedAt > now || now - capture.observedAt >= TimeSpan.FromDays(90) ||
            capture.walletBalances is null || capture.walletBalances.Length is 0 or > 256 ||
            capture.walletBalances.Any(b => b is null || string.IsNullOrEmpty(b.Currency) || b.Currency.Length > 20 ||
                b.Currency.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c))) ||
            capture.walletBalances.Select(b => b.Currency).Distinct().Count() != capture.walletBalances.Length ||
            capture.walletBalances.Any(b => b.Available < 0 || b.Reserved != 0 || b.Total != b.Available) ||
            (receiptMode ? capture.thbTotal < 0 : capture.thbTotal <= 0) || capture.thbAvailable != capture.thbTotal || capture.thbReserved != 0 ||
            capture.walletBalances.Single(b => b.Currency == "THB").Total != capture.thbTotal ||
            capture.marketRules is null || capture.marketRules.Length is 0 or > LiveMarketSymbol.MaximumPayloadMarkets ||
            capture.marketRules.Any(p => p is null) ||
            capture.marketRules.Select(p => p.Symbol).Distinct().Count() != capture.marketRules.Length ||
            capture.marketRules.Any(p => !LiveMarketSymbol.IsValid(p.Symbol) || p.Status != "active" ||
                p.Source != "exchange" || p.MarketSegment != "SPOT" || p.FreezeBuy || p.FreezeSell || p.FreezeCancel ||
                p.ObservedAt > capture.observedAt || capture.observedAt - p.ObservedAt > TimeSpan.FromSeconds(60)) ||
            capture.orderHistory is null || capture.orderHistory.Length > (receiptMode ? 1000 : 20) || capture.orderDetails is null ||
            capture.orderHistory.Any(h => h is null || string.IsNullOrWhiteSpace(h.TransactionId)) ||
            capture.orderDetails.Any(o => o is null || o.History is null || o.History.Any(h => h is null)) ||
            capture.orderDetails.Length > (receiptMode ? 1000 : 2) || capture.orderHistory.Select(h => h.TransactionId).Distinct().Count() != capture.orderHistory.Length ||
            capture.orderHistory.Any(h => !capture.marketRules.Any(m => m.Symbol == h.Symbol)) ||
            capture.historyCounts is null || capture.historyCounts.Count != capture.marketRules.Length ||
            !capture.historyCounts.Keys.Order(StringComparer.Ordinal).SequenceEqual(capture.marketRules.Select(m => m.Symbol).Order(StringComparer.Ordinal)) ||
            capture.historyCounts.Any(p => p.Value != capture.orderHistory.Count(h => h.Symbol == p.Key))) throw Invalid();
        return capture;
    }

    private static BrokerOrderEvidence ConfirmOrder(Capture capture, string side, DateTimeOffset after)
    {
        var order = capture.orderDetails.Single();
        if (order.Symbol != "BTC-THB" || order.Side != (side == "buy" ? OrderSide.Buy : OrderSide.Sell) ||
            order.Status != "filled" || order.PartiallyFilled || order.RawRemaining != 0 || order.RawAmount <= 0 || order.Rate < 0 ||
            order.RawTotal != order.RawAmount || order.RawFilled <= 0 || order.RawFilled > order.RawTotal || order.OrderId != order.FirstOrderId ||
            order.OrderId != order.LastOrderId || order.ParentOrderId != order.OrderId && order.ParentOrderId != "0" ||
            order.History is null || order.History.Count is 0 or > 8 || order.History.Count != capture.orderHistory.Length ||
            string.IsNullOrEmpty(order.OrderId) || order.OrderId.Length > 256 || order.OrderId.Any(char.IsControl) ||
            order.Rate % .01m != 0 || order.ClientId is null || order.ClientId.Length > 2048 || order.ClientId.Any(char.IsControl) ||
            order.History.Select(h => h.TransactionId).Distinct().Count() != order.History.Count) throw Invalid();
        foreach (var raw in order.History)
        {
            if (raw is null || string.IsNullOrWhiteSpace(raw.TransactionId)) throw Invalid();
            var history = capture.orderHistory.Single(h => h.TransactionId == raw.TransactionId);
            if (raw.OrderId != order.OrderId || history.OrderId != order.OrderId || history.ParentOrderId != order.OrderId ||
                history.SuperOrderId != order.OrderId || history.ClientId != order.ClientId || history.Side != side ||
                history.Type != (order.Rate == 0 ? "market" : "limit") ||
                history.Rate != raw.Rate || history.RawAmount != raw.RawAmount || history.ReportedFee != raw.ReportedFee ||
                history.FeeCredit != raw.FeeCredit || history.Timestamp != raw.Timestamp ||
                history.Timestamp <= after.ToUnixTimeMilliseconds() || history.Timestamp > capture.observedAt.ToUnixTimeMilliseconds() ||
                raw.Rate <= 0 || raw.Rate % .01m != 0 || raw.RawAmount <= 0 || raw.ReportedFee < 0 || raw.FeeCredit < 0 || raw.FeeCredit > raw.ReportedFee ||
                order.Rate > 0 && (side == "buy" && raw.Rate > order.Rate || side == "sell" && raw.Rate < order.Rate)) throw Invalid();
        }
        if (order.ReportedFee != order.History.Sum(h => h.ReportedFee) || order.FeeCredit != order.History.Sum(h => h.FeeCredit)) throw Invalid();
        return order;
    }

    private static bool SameOrder(BrokerOrderEvidence a, BrokerOrderEvidence b)
        => a with { History = b.History } == b && a.History.SequenceEqual(b.History);

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw Invalid(); RejectDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }

    private static InvalidDataException Invalid() => new("BITKUB_PROFILE_EVIDENCE_NOT_VERIFIED");

    // Wire names match the private capture tool; nested broker records keep their existing names.
    private sealed record Capture(DateTimeOffset observedAt, bool completed, string source, bool walletReadSucceeded,
        bool liveEnabled, decimal thbAvailable, decimal thbReserved, decimal thbTotal, string[] nonzeroCoins,
        bool? tradePermissionConfirmed, int? openOrderCount, Dictionary<string, int> historyCounts, bool? stableWallet,
        BrokerSymbolRules[] marketRules, bool? initialInventoryEmpty, bool funded, string credentialFingerprint,
        BrokerWalletBalance[] walletBalances, BrokerHistoryEntry[] orderHistory, BrokerOrderEvidence[] orderDetails);
}
