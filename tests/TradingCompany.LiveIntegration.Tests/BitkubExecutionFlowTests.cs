using System.Net;
using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class BitkubExecutionFlowTests(PostgresFixture database)
{
    [Fact]
    public async Task Accepted_buy_is_durable_and_reconciles_from_authoritative_fill_evidence()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock) { Accepted = true };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        var intent = template with { AccountId = account.AccountId };
        var service = new LiveExecutionService(store, adapter, clock);

        var first = await service.SubmitAsync(intent);
        var second = await new LiveExecutionService(new LiveTradingStore(database.ConnectionString), adapter, clock).SubmitAsync(intent);
        Assert.True(first.State == LiveOrderState.Acknowledged, string.Join(",", first.Violations));
        Assert.Equal(first.State, second.State);
        Assert.Equal(1, handler.PlacementCount);
        Assert.Equal(1000m, (await store.ReadAsync(account.AccountId)).CashBalance);

        var result = await new LiveReconciliation(store, new BitkubReconciliationSource(broker, adapter, account.AccountId, clock), clock).ReconcileAsync(account.AccountId);
        Assert.True(result.Complete);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(account.AccountId);
        Assert.Equal(302m, restored.Snapshot.CashBalance);
        Assert.Equal(.0069626m, Assert.Single(restored.Snapshot.Positions).Quantity);
        Assert.Equal(1.74m, Assert.Single(restored.Fills).CashFee);
        Assert.Equal(LiveOrderState.Filled, Assert.Single(restored.Orders).State);
    }

    [Fact]
    public async Task Missing_rules_releases_unsent_reservation_without_placing()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock);
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, null);
        var order = await new LiveExecutionService(store, adapter, clock).SubmitAsync(template with { AccountId = account.AccountId });
        Assert.Equal(LiveOrderState.Rejected, order.State);
        Assert.Equal(0, handler.PlacementCount);
        Assert.Equal(0m, (await store.ReadAsync(account.AccountId)).CashReserved);
    }

    [Fact]
    public async Task Lost_placement_reply_is_recovered_from_history_without_resend_or_rearm()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock) { Accepted = true, LosePlacementReply = true };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        var intent = template with { AccountId = account.AccountId };
        var first = await new LiveExecutionService(store, adapter, clock).SubmitAsync(intent);
        Assert.Equal(LiveOrderState.Unknown, first.State);
        await new LiveExecutionService(store, adapter, clock).SuspendForRecoveryAsync(account.AccountId);
        var source = new BitkubReconciliationSource(broker, adapter, account.AccountId, clock);
        Assert.True((await new LiveReconciliation(store, source, clock).ReconcileAsync(account.AccountId)).Complete);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(account.AccountId);
        Assert.Equal(LiveOrderState.Filled, Assert.Single(restored.Orders).State);
        Assert.False(restored.Gate.Armed);
        Assert.Equal(1, handler.PlacementCount);
    }

    [Fact]
    public async Task Wallet_mismatch_blocks_reconciliation_without_mutating_money()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock) { Accepted = true, WalletMismatch = true };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        var intent = template with { AccountId = account.AccountId };
        Assert.Equal(LiveOrderState.Acknowledged, (await new LiveExecutionService(store, adapter, clock).SubmitAsync(intent)).State);
        var result = await new LiveReconciliation(store, new BitkubReconciliationSource(broker, adapter, account.AccountId, clock), clock).ReconcileAsync(account.AccountId);
        Assert.False(result.Complete);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(account.AccountId);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Empty(restored.Fills);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
    }

    [Fact]
    public async Task Unverified_symbol_or_changed_market_rules_never_places()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock) { ChangedMarketRules = true };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.PrepareAsync(template with { AccountId = account.AccountId, Symbol = "ETH-THB" }, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.PrepareAsync(template with { AccountId = account.AccountId }, default));
        Assert.Equal(0, handler.PlacementCount);
    }

    [Fact]
    public async Task Rotated_credentials_block_prepare_before_private_http()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock);
        using var broker = new BitkubLiveBroker("rotated-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.PrepareAsync(template with { AccountId = account.AccountId }, default));
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Conflicting_fee_history_blocks_reconciliation_atomically()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock) { Accepted = true, ConflictingFee = true };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        Assert.Equal(LiveOrderState.Acknowledged, (await new LiveExecutionService(store, adapter, clock).SubmitAsync(template with { AccountId = account.AccountId })).State);
        var result = await new LiveReconciliation(store, new BitkubReconciliationSource(broker, adapter, account.AccountId, clock), clock).ReconcileAsync(account.AccountId);
        Assert.False(result.Complete);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(account.AccountId);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Empty(restored.Fills);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
    }

    [Fact]
    public void Normalize_buy_fee_credit_uses_net_cash_and_exact_quantity()
    {
        var account = new LiveAccountId(Guid.NewGuid());
        var intent = new LiveIntent(account, Guid.NewGuid(), Guid.NewGuid(), TradingMode.Live, "BTC-THB", OrderSide.Buy, .007m, 100000, 1, 1, "e") with { BuyBudget = 700m };
        var raw = new BrokerOrderFillEvidence("order", "txn", 100000, 698, 2, .26m, FixedNow.ToUnixTimeSeconds());
        var fill = BitkubReconciliationSource.Normalize(account, intent, "order", raw, Rules());
        Assert.Equal(1.74m, fill.CashFee);
        Assert.Equal(.0069626m, fill.Quantity);
    }

    [Fact]
    public void Normalize_confirmed_gross_buy_does_not_deduct_cash_fee_from_coin_quantity()
    {
        var account = new LiveAccountId(Guid.NewGuid());
        var intent = new LiveIntent(account, Guid.NewGuid(), Guid.NewGuid(), TradingMode.Live, "BTC-THB",
            OrderSide.Buy, .007m, 100000, 1, 1, "synthetic") { BuyBudget = 700m };
        var fill = BitkubReconciliationSource.Normalize(account, intent, "order",
            new("order", "txn", 100000, 698, 2, .26m, FixedNow.ToUnixTimeSeconds()),
            Rules() with { BuyHistoryAmount = BitkubBuyHistoryAmount.QuoteBeforeCashFee });
        Assert.Equal(.00698m, fill.Quantity);
        Assert.Equal(1.74m, fill.CashFee);
        Assert.Equal(699.74m, fill.Quantity * fill.Price + fill.CashFee);
    }

    [Fact]
    public void Normalize_sell_keeps_base_amount_and_reports_net_cash_fee()
    {
        var account = new LiveAccountId(Guid.NewGuid());
        var intent = new LiveIntent(account, Guid.NewGuid(), Guid.NewGuid(), TradingMode.Live, "BTC-THB",
            OrderSide.Sell, .00698m, 100000, 1, 1, "synthetic");
        var fill = BitkubReconciliationSource.Normalize(account, intent, "order",
            new("order", "txn", 100000, .00698m, 2, .26m, FixedNow.ToUnixTimeSeconds()), Rules());
        Assert.Equal(.00698m, fill.Quantity);
        Assert.Equal(1.74m, fill.CashFee);
        Assert.Equal(696.26m, fill.Quantity * fill.Price - fill.CashFee);
    }

    [Fact]
    public async Task Direct_place_without_durable_dispatch_proof_is_rejected_without_post()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock) { Accepted = true };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        var prepared = await adapter.PrepareAsync(template with { AccountId = account.AccountId }, default);
        var result = await adapter.PlaceAsync(prepared, default);
        Assert.Equal(BrokerSubmissionState.Rejected, result.State);
        Assert.Equal(0, handler.PlacementCount);
    }

    [Fact]
    public async Task Reduced_limits_cannot_prevent_owned_cancellation_or_recovery_of_existing_fills()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock) { Accepted = true };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        var service = new LiveExecutionService(store, adapter, clock);
        Assert.Equal(LiveOrderState.Acknowledged, (await service.SubmitAsync(template)).State);
        await store.WithAccountLockAsync(account.AccountId, async (tx, ct) =>
        {
            Assert.True(tx.SaveMandate(tx.Account.Mandate with { Version = 2, FeeReserveRate = .001m }, FixedNow).Allowed);
            await tx.CommitAsync(ct);
            return 0;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.PrepareAsync(template, default));
        var cancelled = await service.CancelOwnedAsync(account.AccountId, template.Id);
        Assert.Equal(BrokerSubmissionState.Accepted, cancelled.CancellationOutcome);
        Assert.Equal(702.1m, cancelled.ReservedCash);
        Assert.Single(handler.Paths, p => p.EndsWith("/cancel-order"));
        // The fixture models a fill winning the cancellation race. Acknowledgement never frees cash.
        Assert.True((await new LiveReconciliation(store, new BitkubReconciliationSource(broker, adapter,
            account.AccountId, clock), clock).ReconcileAsync(account.AccountId)).Complete);
        Assert.Equal(302m, (await store.ReadAsync(account.AccountId)).CashBalance);
        Assert.Equal(1, handler.PlacementCount);
    }

    [Fact]
    public async Task Lost_reply_without_authoritative_history_stays_unknown_and_is_not_retried()
    {
        var (store, account, template) = await Setup();
        var clock = new LiveExecutionFlowTests.TestClock(FixedNow);
        var handler = new BitkubHandler(clock) { Accepted = true, LosePlacementReply = true, HistoryUnavailable = true };
        using var broker = new BitkubLiveBroker("test-key", "test-secret", handler, clock);
        var adapter = new BitkubExecutionAdapter(broker, store, account.AccountId, clock, Rules());
        var intent = template with { AccountId = account.AccountId };
        Assert.Equal(LiveOrderState.Unknown, (await new LiveExecutionService(store, adapter, clock).SubmitAsync(intent)).State);
        await new LiveExecutionService(store, adapter, clock).SuspendForRecoveryAsync(account.AccountId);
        Assert.False((await new LiveReconciliation(store, new BitkubReconciliationSource(broker, adapter, account.AccountId, clock), clock).ReconcileAsync(account.AccountId)).Complete);
        Assert.Equal(LiveOrderState.Unknown, (await new LiveExecutionService(new LiveTradingStore(database.ConnectionString), adapter, clock).SubmitAsync(intent)).State);
        Assert.Equal(1, handler.PlacementCount);
        Assert.Equal(702.1m, (await store.ReadAccountAsync(account.AccountId)).Snapshot.CashReserved);
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 10, 5, 5, 0, 0, TimeSpan.Zero);

    private async Task<(LiveTradingStore Store, LiveAccountState Account, LiveIntent Intent)> Setup()
    {
        var (store, template, intent) = await new LiveTradingStoreTests(database).Setup();
        var id = new LiveAccountId(Guid.NewGuid());
        var observed = FixedNow;
        var account = template with
        {
            AccountId = id,
            Mandate = template.Mandate with { AccountId = id, CredentialFingerprint = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("test-key\0test-secret"u8.ToArray())) },
            Gate = template.Gate with { CredentialFingerprint = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("test-key\0test-secret"u8.ToArray())), Armed = true, Stopped = false },
            Snapshot = template.Snapshot with { AccountId = id, Reconciled = true, ObservedAt = observed, Quotes = [new("BTC-THB", 99999, 100000, observed)] },
            PerformanceBaseline = new(DateOnly.FromDateTime(FixedNow.ToOffset(TimeSpan.FromHours(7)).DateTime), 1000m, 1000m), FirstActivatedAt = observed.AddMinutes(-1)
        };
        await store.CreateAsync(account);
        return (store, account, intent with { AccountId = id, BuyBudget = 700m });
    }

    private static BitkubVerifiedExecutionRules Rules() => new("synthetic-confirmation", new string('a', 64), .0025m,
        BitkubBuyHistoryAmount.QuoteIncludingCashFee, [new("BTC-THB", 1, .00000001m, .01m, 10, 0, 1)]);

    private sealed class BitkubHandler(LiveExecutionFlowTests.TestClock clock) : HttpMessageHandler
    {
        public bool Accepted { get; init; }
        public bool LosePlacementReply { get; init; }
        public bool WalletMismatch { get; init; }
        public bool ChangedMarketRules { get; init; }
        public bool ConflictingFee { get; init; }
        public bool HistoryUnavailable { get; init; }
        public List<string> Paths { get; } = [];
        public int PlacementCount { get; private set; }
        private string ClientId { get; set; } = "CLIENT";
        private const string OrderId = "order-1";
        private const string TxnId = "txn-1";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.EndsWith("/servertime")) return Reply(clock.Now.ToUnixTimeMilliseconds().ToString());
            if (path.EndsWith("/symbols")) return Reply("{\"error\":0,\"result\":[{\"symbol\":\"BTC_THB\",\"base_asset\":\"BTC\",\"quote_asset\":\"THB\",\"base_asset_scale\":8,\"quote_asset_scale\":2,\"price_scale\":2,\"quantity_scale\":0,\"price_step\":\"" + (ChangedMarketRules ? "0.1" : "0.01") + "\",\"quantity_step\":\"1\",\"min_quote_size\":10,\"freeze_buy\":false,\"freeze_sell\":false,\"freeze_cancel\":false,\"status\":\"active\",\"source\":\"exchange\",\"market_segment\":\"SPOT\"}]}");
            if (path.EndsWith("/place-bid")) { PlacementCount++; ClientId = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.GetProperty("client_id").GetString()!; if (LosePlacementReply) throw new HttpRequestException("reply lost"); return Reply(Accepted ? "{\"error\":0,\"result\":{\"id\":\"order-1\",\"typ\":\"limit\",\"amt\":698,\"rat\":100000,\"ci\":\"" + ClientId + "\",\"ts\":\"1\",\"fee\":0,\"cre\":0,\"rec\":0}}" : "{\"error\":1,\"result\":null}"); }
            if (path.EndsWith("/my-open-orders")) return Reply("{\"error\":0,\"result\":[]}");
            if (path.EndsWith("/cancel-order")) return Reply("{\"error\":0}");
            var ts = clock.Now.ToUnixTimeSeconds();
            if (path.EndsWith("/my-order-history")) return Reply(HistoryUnavailable ? "{\"error\":0,\"result\":[],\"pagination\":{\"has_next\":false}}" : "{\"error\":0,\"result\":[{\"txn_id\":\"txn-1\",\"order_id\":\"order-1\",\"parent_order_id\":\"order-1\",\"super_order_id\":\"order-1\",\"client_id\":\"" + ClientId + "\",\"side\":\"buy\",\"type\":\"limit\",\"rate\":\"100000\",\"amount\":\"698\",\"fee\":\"" + (ConflictingFee ? "1.75" : "1.74") + "\",\"credit\":\"0\",\"ts\":" + ts + "}],\"pagination\":{\"has_next\":false}}");
            if (path.EndsWith("/order-info")) return Reply("{\"error\":0,\"result\":{\"id\":\"order-1\",\"first\":\"order-1\",\"parent\":\"order-1\",\"last\":\"order-1\",\"client_id\":\"" + ClientId + "\",\"post_only\":false,\"amount\":\"698\",\"rate\":100000,\"fee\":1.74,\"credit\":0,\"filled\":698,\"total\":698,\"status\":\"filled\",\"partial_filled\":false,\"remaining\":0,\"history\":[{\"amount\":698,\"credit\":0,\"fee\":1.74,\"id\":\"order-1\",\"rate\":100000,\"timestamp\":" + ts + ",\"txn_id\":\"txn-1\"}]}}");
            if (path.EndsWith("/balances")) return Reply(WalletMismatch ? "{\"code\":\"0\",\"data\":[{\"currency\":\"THB\",\"available\":\"301\",\"reserved\":\"0\",\"total\":\"301\"},{\"currency\":\"BTC\",\"available\":\"0.0069626\",\"reserved\":\"0\",\"total\":\"0.0069626\"}]}" : "{\"code\":\"0\",\"data\":[{\"currency\":\"THB\",\"available\":\"302\",\"reserved\":\"0\",\"total\":\"302\"},{\"currency\":\"BTC\",\"available\":\"0.0069626\",\"reserved\":\"0\",\"total\":\"0.0069626\"}]}");
            if (path.EndsWith("/ticker")) return Reply("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"99999\",\"lowest_ask\":\"100000\"}]");
            throw new InvalidOperationException(path);
        }
        private static Task<HttpResponseMessage> Reply(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
