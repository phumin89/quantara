using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveReconciliationFlowTests(PostgresFixture database)
{
    [Fact]
    public async Task Initial_baseline_and_evidence_persist_together_and_concurrent_initialization_cannot_reset_it()
    {
        var (store, template, _) = await new LiveTradingStoreTests(database).Setup();
        var id = new LiveAccountId(Guid.NewGuid());
        var account = template with
        {
            AccountId = id, Mandate = template.Mandate with { AccountId = id },
            Gate = template.Gate with { Armed = false, Stopped = true },
            Snapshot = template.Snapshot with { AccountId = id, Reconciled = false },
            PerformanceBaseline = null
        };
        await store.CreateAsync(account);
        var evidence = new LiveBrokerEvidence(id, account.Mandate.CredentialFingerprint,
            account.Snapshot.ObservedAt, true, 1000m, 0m, [], [], [], account.Snapshot.Quotes);
        var request = new LiveBaselineRequest(id, account.Mandate.Version, account.Snapshot.Version);
        var clock = new LiveExecutionFlowTests.TestClock(account.Snapshot.ObservedAt);
        var service = new LiveReconciliation(store, new Source(evidence), clock);
        var results = await Task.WhenAll(service.InitializeBaselineAsync(request), service.InitializeBaselineAsync(request));
        Assert.Single(results, r => r.Complete);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(id);
        Assert.Equal(new LivePerformanceBaseline(new(2026, 9, 27), 1000m, 1000m), restored.PerformanceBaseline);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.False(restored.Gate.Armed);
        Assert.True(restored.Gate.Stopped);
        Assert.Null(restored.FirstActivatedAt);
        Assert.Single(restored.Audit, a => a.Kind == "InitialPerformanceBaselineEstablished");
        Assert.Single(restored.Audit, a => a.Kind == "ReconciliationEvidence");
        Assert.Equal(account.Mandate.CapitalAllocation, restored.Mandate.CapitalAllocation);
    }

    [Fact]
    public async Task Two_stable_reads_commit_fill_order_cash_and_evidence_together_without_rearming()
    {
        var (store, account, intent, clock) = await Setup();
        var source = new Source(Evidence(account, intent));
        var service = new LiveReconciliation(store, source, clock);
        Assert.True((await service.ReconcileAsync(account.AccountId)).Complete);
        Assert.Equal(2, source.Reads);
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(account.AccountId);
        Assert.Equal(899.75m, restored.Snapshot.CashBalance);
        Assert.Equal(.001m, Assert.Single(restored.Snapshot.Positions).Quantity);
        Assert.Equal(LiveOrderState.Filled, Assert.Single(restored.Orders).State);
        Assert.Single(restored.Fills);
        Assert.Equal(0m, restored.Snapshot.CashReserved);
        Assert.False(restored.Gate.Armed);
        Assert.True(restored.Snapshot.Reconciled);
        Assert.Contains(restored.Audit, a => a.Kind == "ReconciliationEvidence");
        Assert.True((await service.ReconcileAsync(account.AccountId)).Complete);
        Assert.Single((await store.ReadAccountAsync(account.AccountId)).Fills);
    }

    [Fact]
    public async Task Incomplete_remote_read_durably_disarms_without_leaking_private_error_or_mutating_money()
    {
        var (store, account, _, clock) = await Setup();
        var source = new Source(null);
        var result = await new LiveReconciliation(store, source, clock).ReconcileAsync(account.AccountId);
        Assert.False(result.Complete);
        Assert.Equal(1, source.Reads);
        var restored = await store.ReadAccountAsync(account.AccountId);
        Assert.Equal(1000m, restored.Snapshot.CashBalance);
        Assert.Equal(100.3m, restored.Snapshot.CashReserved);
        Assert.Empty(restored.Fills);
        Assert.False(restored.Gate.Armed);
        Assert.False(restored.Snapshot.Reconciled);
        Assert.DoesNotContain(restored.Audit, a => a.Detail.Contains("private-error"));
    }

    private async Task<(LiveTradingStore Store, LiveAccountState Account, LiveIntent Intent, LiveExecutionFlowTests.TestClock Clock)> Setup()
    {
        var (store, template, initialIntent) = await new LiveTradingStoreTests(database).Setup();
        var id = new LiveAccountId(Guid.NewGuid());
        var account = template with
        {
            AccountId = id, Mandate = template.Mandate with { AccountId = id },
            Snapshot = template.Snapshot with { AccountId = id },
            PerformanceBaseline = new(new(2026, 9, 27), 1000m, 1000m)
        };
        await store.CreateAsync(account);
        var intent = initialIntent with { AccountId = id, Quantity = .001m };
        var clock = new LiveExecutionFlowTests.TestClock(account.Snapshot.ObservedAt);
        await new LiveExecutionService(store, new LiveExecutionFlowTests.FakeBroker(), clock).SubmitAsync(intent);
        await store.WithAccountLockAsync(id, async (tx, ct) => { tx.SetStopped(true); await tx.CommitAsync(ct); return 0; });
        return (store, account, intent, clock);
    }

    private static LiveBrokerEvidence Evidence(LiveAccountState account, LiveIntent intent) => new(account.AccountId,
        account.Mandate.CredentialFingerprint, account.Snapshot.ObservedAt, true, 899.75m, 0m,
        [new("BTC-THB", .001m, 0m)],
        [new(intent.Id.ToString("N"), "broker-1", "BTC-THB", OrderSide.Buy, .001m, 100000m, LiveOrderState.Filled, .001m, 0m, 0m)],
        [new(account.AccountId, "fill", "broker-1", OrderSide.Buy, "BTC-THB", .001m, 100000m, .25m, 1)], account.Snapshot.Quotes);

    private sealed class Source(LiveBrokerEvidence? evidence) : ILiveReconciliationSource
    {
        public int Reads { get; private set; }
        public Task<LiveBrokerEvidence> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken)
        {
            Reads++;
            return evidence is null ? throw new HttpRequestException("private-error") : Task.FromResult(evidence);
        }
    }
}
