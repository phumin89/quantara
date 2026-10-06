using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveDailyBaselinePersistenceTests(PostgresFixture database)
{
    [Fact]
    public async Task Automatic_rollover_persists_across_a_fresh_store_and_retains_valid_authority()
    {
        var store = new LiveTradingStore(database.ConnectionString);
        await store.EnsureSchemaAsync();
        var id = new LiveAccountId(Guid.NewGuid());
        var previous = new DateTimeOffset(2026, 9, 27, 23, 59, 45, TimeSpan.FromHours(7));
        var now = new DateTimeOffset(2026, 9, 28, 0, 0, 15, TimeSpan.FromHours(7));
        var mandate = new LiveMandate(id, 1, "test-key", 1000, 800, 5000, 100, .2m, .8m, 3, .01m, .003m,
            ["BTC-THB"], null) { AutomaticDailyRollover = true };
        var snapshot = new LiveSnapshot(id, 1, previous, 1000, 0, 0, [], 1000, 0, 0, 0, true,
            [new("BTC-THB", 99900, 100000, previous)]);
        var account = new LiveAccountState(id, mandate, new(true, false, "test-key", true, 1), snapshot, [], [], [])
        { PerformanceBaseline = new(new(2026, 9, 27), 1000, 1200) };
        await store.CreateAsync(account);

        var evidence = new LiveBrokerEvidence(id, "test-key", now, true, 1000, 0, [], [], [],
            [new("BTC-THB", 99900, 100000, now)]);
        await store.WithAccountLockAsync(id, async (tx, ct) =>
        {
            var result = tx.Reconcile(evidence, evidence, now);
            Assert.True(result.Complete);
            await tx.CommitAsync(ct);
            return 0;
        });

        var fresh = new LiveTradingStore(database.ConnectionString);
        var restored = await fresh.ReadAccountAsync(id);
        Assert.Equal(new LivePerformanceBaseline(new(2026, 9, 28), 1000, 1200), restored.PerformanceBaseline);
        Assert.Equal(0, restored.Snapshot.DailyLoss);
        Assert.Equal(0, restored.Snapshot.DailyTurnover);
        Assert.Equal(1m / 6m, restored.Snapshot.Drawdown);
        Assert.True(restored.Gate.Armed);
        Assert.Equal(1000, restored.Snapshot.CashBalance);
        Assert.Equal(1200, restored.PerformanceBaseline!.HighWaterEquity);
        Assert.Single(restored.Audit, audit => audit.Kind == "DailyPerformanceBaselineEstablished");
        Assert.Empty(restored.Orders);
        Assert.Empty(restored.Fills);
        Assert.Equal(account.Snapshot.Positions.Select(p => p.Symbol), restored.Snapshot.Positions.Select(p => p.Symbol));
    }
}
