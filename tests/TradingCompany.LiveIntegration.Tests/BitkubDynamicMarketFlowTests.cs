using System.Globalization;
using System.Net;
using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class BitkubDynamicMarketFlowTests(PostgresFixture database)
{
    [Theory]
    [InlineData("USDT-THB", "33.12", "2.12", 2, 2, false)]
    [InlineData("XRP-THB", "2.12345", "50.12345678", 5, 8, false)]
    [InlineData("BTC-THB", "2800287.93", "0.00001040", 2, 8, true)]
    [InlineData("BTC-THB", "2800287.93", "0.00001040", 2, 8, false)]
    public async Task Owned_native_sell_preserves_base_units_and_net_THB_receipt_even_when_buys_are_frozen(
        string symbol, string priceText, string quantityText, int priceScale, int baseScale, bool credited)
    {
        var (store, template, original) = await new LiveTradingStoreTests(database).Setup();
        var clock = new LiveExecutionFlowTests.TestClock(template.Snapshot.ObservedAt);
        var price = decimal.Parse(priceText, CultureInfo.InvariantCulture);
        var quantity = decimal.Parse(quantityText, CultureInfo.InvariantCulture);
        var gross = quantity * price;
        var initialCash = 1000m - gross;
        var handler = new NativeMarketHandler(symbol, price, priceScale, baseScale, clock, OrderSide.Sell, initialCash)
            { FreezeBuy = true, SellFeeOverride = symbol == "BTC-THB" ? .08m : null, CreditUsed = credited ? .08m : 0m };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var id = new LiveAccountId(Guid.NewGuid());
        var account = template with
        {
            AccountId = id,
            Mandate = template.Mandate with { AccountId = id, CredentialFingerprint = broker.CredentialFingerprint,
                AllowedSymbols = [symbol], FeeReserveRate = .0035m },
            Gate = template.Gate with { CredentialFingerprint = broker.CredentialFingerprint },
            Snapshot = template.Snapshot with { AccountId = id, CashBalance = initialCash,
                Positions = [new(symbol, quantity, 0, price)], Quotes = [new(symbol, price, price + Step(priceScale), clock.Now)] },
            FirstActivatedAt = clock.Now.AddMinutes(-1)
        };
        await store.CreateAsync(account);
        var rules = new BitkubVerifiedExecutionRules("synthetic-native-sell", new string('a', 64), .0035m,
            BitkubBuyHistoryAmount.AuthenticatedOrderReceipts,
            [new(symbol, 1, Step(baseScale), Step(priceScale), 10, 0, 1) { BuyBaseRounding = BitkubBuyBaseRounding.FloorToBaseStep }]);
        var adapter = new BitkubExecutionAdapter(broker, store, id, clock, rules, receiptStreamReady: () => true);
        var intent = original with { AccountId = id, Symbol = symbol, Side = OrderSide.Sell, Quantity = quantity, LimitPrice = price };
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.PrepareAsync(
            intent with { Quantity = quantity + Step(baseScale) / 10m }, default));
        var order = await new LiveExecutionService(store, adapter, clock).SubmitAsync(intent);
        Assert.Equal(LiveOrderState.Acknowledged, order.State);
        Assert.Equal(quantity, handler.WireAmount);
        Assert.Equal(1, handler.PlacementCount);
        Assert.Null(order.Intent.BuyBudget);
        Assert.Equal(initialCash, (await store.ReadAsync(id)).CashBalance);
        var receipt = new BitkubOrderReceipt("native-order", intent.Id.ToString("N"), symbol, OrderSide.Sell, "filled",
            price, quantity, quantity, handler.Received, handler.TotalFee, handler.CreditUsed, handler.CashFee,
            clock.Now.ToUnixTimeMilliseconds());
        var journal = new BitkubOrderReceiptJournal(database.ConnectionString, id, broker.CredentialFingerprint);
        await journal.AppendAsync(receipt);
        var reconciliation = new LiveReconciliation(store, new BitkubReconciliationSource(broker, adapter, id, clock, journal), clock);
        var result = await reconciliation.ReconcileAsync(id);
        Assert.True(result.Complete, string.Join(",", result.Violations));
        var restarted = new LiveTradingStore(database.ConnectionString);
        var restored = await restarted.ReadAccountAsync(id);
        var fill = Assert.Single(restored.Fills);
        Assert.Equal(OrderSide.Sell, fill.Side);
        Assert.Equal(symbol, fill.Symbol);
        Assert.Equal(quantity, fill.Quantity);
        Assert.Equal(price, fill.Price);
        Assert.Equal(gross, fill.GrossQuoteAmount);
        var adjustment = Assert.Single(restored.Orders).SellQuoteAdjustment;
        Assert.Equal(receipt.ReceivedAmount, fill.GrossQuoteAmount + adjustment - fill.CashFee);
        Assert.NotEqual(0m, adjustment);
        Assert.Equal(initialCash + receipt.ReceivedAmount, restored.Snapshot.CashBalance);
        Assert.Empty(restored.Snapshot.Positions);
        Assert.Equal(LiveOrderState.Filled, Assert.Single(restored.Orders).State);
        Assert.Equal(JsonSerializer.Serialize(account.Mandate), JsonSerializer.Serialize(restored.Mandate));
        Assert.Equal(account.Gate, restored.Gate);
        var restartedJournal = new BitkubOrderReceiptJournal(database.ConnectionString, id, broker.CredentialFingerprint);
        Assert.Equal(receipt, await restartedJournal.ReadLatestAsync(receipt.OrderId, receipt.ClientId));
        Assert.True((await new LiveReconciliation(restarted,
            new BitkubReconciliationSource(broker, adapter, id, clock, restartedJournal), clock).ReconcileAsync(id)).Complete);
        Assert.Single((await restarted.ReadAccountAsync(id)).Fills);
        var repeated = await restarted.ReadAccountAsync(id);
        Assert.Equal(restored.Snapshot.CashBalance, repeated.Snapshot.CashBalance);
        Assert.Equal(restored.Snapshot.DailyTurnover, repeated.Snapshot.DailyTurnover);
        Assert.Equal(adjustment, Assert.Single(repeated.Orders).SellQuoteAdjustment);
        Assert.Equal(1, handler.PlacementCount);
    }

    [Theory]
    [InlineData("XRP-THB", "2.12345", 5, 8, 50)]
    [InlineData("DOGE-THB", "1.2345678", 7, 8, 50)]
    [InlineData("USDT-THB", "33.12", 2, 2, 3)]
    public async Task Native_precision_fake_placement_and_receipt_reconcile_durably(string symbol, string priceText,
        int priceScale, int baseScale, int quantity)
    {
        var (store, template, _) = await new LiveTradingStoreTests(database).Setup();
        var clock = new LiveExecutionFlowTests.TestClock(template.Snapshot.ObservedAt);
        var price = decimal.Parse(priceText, CultureInfo.InvariantCulture);
        var handler = new NativeMarketHandler(symbol, price, priceScale, baseScale, clock);
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var id = new LiveAccountId(Guid.NewGuid());
        var account = template with
        {
            AccountId = id,
            Mandate = template.Mandate with { AccountId = id, CredentialFingerprint = broker.CredentialFingerprint, AllowedSymbols = [symbol] },
            Gate = template.Gate with { CredentialFingerprint = broker.CredentialFingerprint },
            Snapshot = template.Snapshot with { AccountId = id, Quotes = [new(symbol, price - Step(priceScale), price, clock.Now)] },
            FirstActivatedAt = clock.Now.AddMinutes(-1)
        };
        await store.CreateAsync(account);
        var rules = new BitkubVerifiedExecutionRules("synthetic-native-market", new string('a', 64), .0025m,
            BitkubBuyHistoryAmount.AuthenticatedOrderReceipts,
            [new(symbol, 1, Step(baseScale), Step(priceScale), 10, 0, 1) { BuyBaseRounding = BitkubBuyBaseRounding.FloorToBaseStep }]);
        var sizing = new LiveOrderSizing(symbol, Step(priceScale), Step(baseScale), 1, 10);
        var adapter = new BitkubExecutionAdapter(broker, store, id, clock, rules, receiptStreamReady: () => true);
        var service = Service(store, adapter, clock, sizing);
        var plan = new LiveTradePlanRequest(id, Guid.NewGuid(), Guid.NewGuid(), account.Mandate.Version, account.Snapshot.Version,
            symbol, new string('a', 64), new(price, quantity,
                decimal.Ceiling(price * 1.1m / sizing.PriceStep) * sizing.PriceStep,
                decimal.Floor(price * .9m / sizing.PriceStep) * sizing.PriceStep, clock.Now.AddHours(4)));
        var registration = await service.RegisterAsync(plan);
        Assert.True(registration.Accepted, string.Join(",", registration.Violations));
        await Service(new LiveTradingStore(database.ConnectionString), adapter, clock, sizing).TickAsync(id);
        var order = Assert.Single((await store.ReadAccountAsync(id)).Orders);
        Assert.Equal(LiveOrderState.Acknowledged, order.State);
        Assert.Equal(plan.Id, order.Intent.TradePlanId);
        Assert.Equal(price, order.Intent.LimitPrice);
        Assert.Equal(price * quantity, order.Intent.BuyBudget);
        Assert.Equal(decimal.Floor(price * quantity / (1 + rules.MaximumCashFeeRate)), handler.WireAmount);
        Assert.Equal(1, handler.PlacementCount);

        var journal = new BitkubOrderReceiptJournal(database.ConnectionString, id, broker.CredentialFingerprint);
        var reconciliation = new LiveReconciliation(store, new BitkubReconciliationSource(broker, adapter, id, clock, journal), clock);
        Assert.False((await reconciliation.ReconcileAsync(id)).Complete);
        Assert.Equal(1000m, (await store.ReadAsync(id)).CashBalance);
        var receipt = new BitkubOrderReceipt("native-order", order.Intent.Id.ToString("N"), symbol, OrderSide.Buy, "filled",
            price, handler.WireAmount, handler.WireAmount, handler.Received, handler.CashFee, 0, handler.CashFee,
            clock.Now.ToUnixTimeMilliseconds());
        await journal.AppendAsync(receipt);
        var result = await reconciliation.ReconcileAsync(id);
        Assert.True(result.Complete, string.Join(",", result.Violations));
        var restarted = new LiveTradingStore(database.ConnectionString);
        var restored = await restarted.ReadAccountAsync(id);
        var fill = Assert.Single(restored.Fills);
        Assert.Equal(symbol, fill.Symbol);
        Assert.Equal(handler.Received, fill.Quantity);
        Assert.Equal(handler.WireAmount - handler.CashFee, fill.GrossQuoteAmount);
        Assert.Equal(handler.WireAmount, fill.GrossQuoteAmount + fill.CashFee);
        Assert.Equal(1000m - handler.WireAmount, restored.Snapshot.CashBalance);
        Assert.Equal(handler.Received, Assert.Single(restored.Snapshot.Positions).Quantity);
        Assert.Equal(LiveOrderState.Filled, Assert.Single(restored.Orders).State);
        Assert.Equal(plan, Assert.Single(LiveTradePlans.Read(restored)));
        Assert.Equal(JsonSerializer.Serialize(account.Mandate), JsonSerializer.Serialize(restored.Mandate));
        Assert.False(restored.Gate.Armed);
        Assert.Equal(account.Gate.CredentialFingerprint, restored.Gate.CredentialFingerprint);
        var restartedJournal = new BitkubOrderReceiptJournal(database.ConnectionString, id, broker.CredentialFingerprint);
        Assert.Equal(receipt, await restartedJournal.ReadLatestAsync(receipt.OrderId, receipt.ClientId));
        Assert.True((await new LiveReconciliation(restarted,
            new BitkubReconciliationSource(broker, adapter, id, clock, restartedJournal), clock).ReconcileAsync(id)).Complete);
        Assert.Single((await restarted.ReadAccountAsync(id)).Fills);
        await Service(restarted, adapter, clock, sizing).TickAsync(id);
        Assert.Equal(1, handler.PlacementCount);
    }

    [Theory]
    [InlineData("XRP-THB", 5)]
    [InlineData("DOGE-THB", 7)]
    public async Task New_market_plan_and_fake_order_survive_reload_with_owner_authority_intact(string symbol, int priceScale)
    {
        var f = await Setup(symbol, priceScale);
        var before = await f.Store.ReadAccountAsync(f.Plan.AccountId);
        Assert.True((await f.Service.RegisterAsync(f.Plan)).Accepted);
        Assert.Equal(0, f.Broker.PlacementCount);

        var restarted = new LiveTradingStore(database.ConnectionString);
        var service = Service(restarted, f.Broker, f.Clock, f.Rules);
        var reloaded = await restarted.ReadAccountAsync(f.Plan.AccountId);
        Assert.Equal(f.Plan, Assert.Single(LiveTradePlans.Read(reloaded)));
        Assert.Equal(JsonSerializer.Serialize(before.Mandate), JsonSerializer.Serialize(reloaded.Mandate));
        Assert.Equal(before.Gate, reloaded.Gate);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RegisterAsync(
            f.Plan with { Plan = f.Plan.Plan with { Quantity = 70 } }));

        await service.TickAsync(f.Plan.AccountId);
        var after = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(f.Plan.AccountId);
        var order = Assert.Single(after.Orders);
        Assert.Equal(symbol, order.Intent.Symbol);
        Assert.Equal(f.Plan.Id, order.Intent.TradePlanId);
        Assert.Equal(LiveTradePlans.IntentId(f.Plan.Id, OrderSide.Buy), order.Intent.Id);
        Assert.Equal(LiveOrderState.Acknowledged, order.State);
        Assert.Equal(100m, order.Intent.BuyBudget);
        Assert.Equal(JsonSerializer.Serialize(before.Mandate), JsonSerializer.Serialize(after.Mandate));
        Assert.Equal(before.Gate, after.Gate);
        Assert.Equal(before.Snapshot.CashBalance, after.Snapshot.CashBalance);
        Assert.Equal(1, f.Broker.PlacementCount);
        Assert.Equal(0, f.Broker.CancellationCount);
        await Service(new LiveTradingStore(database.ConnectionString), f.Broker, f.Clock, f.Rules).TickAsync(f.Plan.AccountId);
        Assert.Equal(1, f.Broker.PlacementCount);
    }

    [Theory]
    [InlineData("allowlist")]
    [InlineData("disarmed")]
    [InlineData("limits")]
    public async Task New_market_cannot_escape_persisted_owner_gates_after_reload(string defect)
    {
        var f = await Setup("XRP-THB", 5);
        await f.Store.WithAccountLockAsync(f.Plan.AccountId, async (tx, token) =>
        {
            if (defect == "disarmed") tx.SuspendForRecovery(f.Clock.Now);
            else
            {
                var changed = tx.Account.Mandate with
                {
                    Version = 2, AllowedSymbols = defect == "allowlist" ? ["BTC-THB"] : ["XRP-THB"],
                    MaximumOrderNotional = defect == "limits" ? 20 : tx.Account.Mandate.MaximumOrderNotional
                };
                Assert.True(tx.SaveMandate(changed, f.Clock.Now).Allowed);
            }
            await tx.CommitAsync(token);
            return 0;
        });
        var restarted = new LiveTradingStore(database.ConnectionString);
        var state = await restarted.ReadAccountAsync(f.Plan.AccountId);
        var plan = f.Plan with { MandateVersion = state.Mandate.Version, SnapshotVersion = state.Snapshot.Version };
        var result = await Service(restarted, f.Broker, f.Clock, f.Rules).RegisterAsync(plan);
        Assert.False(result.Accepted);
        Assert.Contains(defect == "allowlist" ? "SYMBOL_NOT_ALLOWED" :
            defect == "limits" ? "MAX_ORDER_NOTIONAL_EXCEEDED" : "NOT_ARMED", result.Violations);
        Assert.Empty(LiveTradePlans.Read(await restarted.ReadAccountAsync(f.Plan.AccountId)));
        Assert.Empty((await restarted.ReadAccountAsync(f.Plan.AccountId)).Orders);
        Assert.Equal(0, f.Broker.PlacementCount);
    }

    private async Task<(LiveTradingStore Store, LiveTradePlanService Service, LiveTradePlanRequest Plan,
        LiveExecutionFlowTests.FakeBroker Broker, LiveExecutionFlowTests.TestClock Clock, LiveOrderSizing Rules)> Setup(string symbol, int priceScale)
    {
        var (store, original, _) = await new LiveTradingStoreTests(database).Setup();
        var id = new LiveAccountId(Guid.NewGuid());
        var clock = new LiveExecutionFlowTests.TestClock(original.Snapshot.ObservedAt);
        var state = original with
        {
            AccountId = id, Mandate = original.Mandate with { AccountId = id, AllowedSymbols = [symbol] },
            Snapshot = original.Snapshot with { AccountId = id, Quotes = [new(symbol, 1.99999m, 2, clock.Now)] }
        };
        await store.CreateAsync(state);
        var step = 1m;
        for (var i = 0; i < priceScale; i++) step /= 10m;
        var rules = new LiveOrderSizing(symbol, step, .00000001m, 1, 10);
        var broker = new LiveExecutionFlowTests.FakeBroker();
        var plan = new LiveTradePlanRequest(id, Guid.NewGuid(), Guid.NewGuid(), state.Mandate.Version, state.Snapshot.Version,
            symbol, new string('a', 64), new(2, 50, 2.2m, 1.9m, clock.Now.AddHours(4)));
        return (store, Service(store, broker, clock, rules), plan, broker, clock, rules);
    }

    private static LiveTradePlanService Service(ILiveTradingStore store, ILiveBroker broker, TimeProvider clock, LiveOrderSizing rules)
        => new(store, new(store, broker, clock), clock, [rules]);

    private static decimal Step(int scale) { var step = 1m; for (var i = 0; i < scale; i++) step /= 10m; return step; }

    // Every transport response is synthetic. Unexpected paths fail instead of reaching any network.
    internal sealed class NativeMarketHandler(string symbol, decimal price, int priceScale, int baseScale,
        LiveExecutionFlowTests.TestClock clock, OrderSide side = OrderSide.Buy, decimal initialCash = 1000m) : HttpMessageHandler
    {
        public decimal WireAmount { get; private set; }
        public decimal? SellFeeOverride { get; init; }
        public decimal CreditUsed { get; init; }
        public decimal TotalFee => SellFeeOverride ?? WireAmount * (side == OrderSide.Buy ? 1m : price) * .0025m;
        public decimal CashFee => TotalFee - CreditUsed;
        public decimal Received => side == OrderSide.Buy
            ? decimal.Floor((WireAmount - CashFee) / price / Step(baseScale)) * Step(baseScale)
            : decimal.Floor((WireAmount * price - CashFee) * 100m) / 100m;
        public bool FreezeBuy { get; init; }
        public int PlacementCount { get; private set; }
        private string clientId = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var timestamp = clock.Now.ToUnixTimeMilliseconds();
            if (path.EndsWith("/servertime")) return Reply(timestamp.ToString(CultureInfo.InvariantCulture));
            if (path.EndsWith("/wstoken")) return Json(new { error = 0, result = new string('a', 32) });
            if (path.EndsWith("/symbols")) return Json(new { error = 0, result = new[] { new
            {
                symbol = symbol.Replace('-', '_'), base_asset = symbol[..^4], quote_asset = "THB",
                base_asset_scale = baseScale, quote_asset_scale = 2, base_quantity_scale = baseScale, quote_quantity_scale = 2,
                price_scale = priceScale, price_step = Step(priceScale).ToString(CultureInfo.InvariantCulture),
                quantity_scale = 0, quantity_step = "1", min_quote_size = 10, freeze_buy = FreezeBuy,
                freeze_sell = false, freeze_cancel = false, status = "active", source = "exchange", market_segment = "SPOT"
            } } });
            if (path.EndsWith(side == OrderSide.Buy ? "/place-bid" : "/place-ask"))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("test-key", request.Headers.GetValues("X-BTK-APIKEY").Single());
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var data = body.RootElement;
                Assert.Equal(symbol.Replace('-', '_').ToLowerInvariant(), data.GetProperty("sym").GetString());
                Assert.Equal(price, data.GetProperty("rat").GetDecimal());
                WireAmount = data.GetProperty("amt").GetDecimal();
                clientId = data.GetProperty("client_id").GetString()!;
                PlacementCount++;
                return Json(new { error = 0, result = new { id = "native-order", typ = "limit", amt = WireAmount,
                    rat = price, ci = clientId, ts = "1", fee = 0, cre = 0, rec = 0 } });
            }
            if (path.EndsWith("/my-open-orders")) return Json(new { error = 0, result = Array.Empty<object>() });
            if (path.EndsWith("/my-order-history")) return Json(new { error = 0, result = new[] { new
            {
                txn_id = "native-fill", order_id = "native-order", parent_order_id = "native-order", super_order_id = "native-order",
                client_id = clientId, side = side == OrderSide.Buy ? "buy" : "sell", type = "limit", rate = price.ToString(CultureInfo.InvariantCulture),
                amount = WireAmount.ToString(CultureInfo.InvariantCulture), fee = TotalFee.ToString(CultureInfo.InvariantCulture),
                credit = CreditUsed.ToString(CultureInfo.InvariantCulture), ts = timestamp
            } }, pagination = new { has_next = false } });
            if (path.EndsWith("/order-info")) return Json(new { error = 0, result = new
            {
                id = "native-order", first = "native-order", parent = "native-order", last = "native-order", client_id = clientId,
                post_only = false, amount = WireAmount, rate = price, fee = TotalFee, credit = CreditUsed, filled = WireAmount, total = WireAmount,
                status = "filled", partial_filled = false, remaining = 0, history = new[] { new
                { amount = WireAmount, credit = CreditUsed, fee = TotalFee, id = "native-order", rate = price, timestamp, txn_id = "native-fill" } }
            } });
            if (path.EndsWith("/balances")) return Json(new { code = "0", data = new[]
                { Balance("THB", side == OrderSide.Buy ? initialCash - WireAmount : initialCash + Received),
                    Balance(symbol[..^4], side == OrderSide.Buy ? Received : 0m) } });
            if (path.EndsWith("/ticker")) return Json(new[] { new { symbol = symbol.Replace('-', '_'),
                highest_bid = (price - Step(priceScale)).ToString(CultureInfo.InvariantCulture),
                lowest_ask = price.ToString(CultureInfo.InvariantCulture) } });
            throw new InvalidOperationException("Unexpected fake Bitkub operation: " + path);
        }
        private static object Balance(string currency, decimal amount) => new
        { currency, available = amount.ToString(CultureInfo.InvariantCulture), reserved = "0", total = amount.ToString(CultureInfo.InvariantCulture) };
        private static HttpResponseMessage Json(object value) => Reply(JsonSerializer.Serialize(value));
        private static HttpResponseMessage Reply(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    }
}
