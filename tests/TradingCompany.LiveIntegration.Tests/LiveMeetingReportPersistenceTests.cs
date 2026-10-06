using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveMeetingReportPersistenceTests(PostgresFixture database)
{
    [Fact]
    public async Task Meeting_report_survives_fresh_store_restart_and_replay_is_idempotent()
    {
        var (store, account) = await Setup();
        var report = Report(account.AccountId);
        await store.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(report, Now); await tx.CommitAsync(ct); return 0; });

        var restarted = new LiveTradingStore(database.ConnectionString);
        var restored = await restarted.ReadAccountAsync(account.AccountId);
        Assert.Equal(report, Assert.Single(LiveMeetingReports.Read(restored)));
        Assert.Equal(account.Snapshot.CashBalance, restored.Snapshot.CashBalance);
        Assert.Equal(account.Gate, restored.Gate);

        await restarted.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(report, Now); await tx.CommitAsync(ct); return 0; });
        var replayed = await restarted.ReadAccountAsync(account.AccountId);
        Assert.Single(LiveMeetingReports.Read(replayed));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(restored.Snapshot), System.Text.Json.JsonSerializer.Serialize(replayed.Snapshot));
        Assert.Equal(restored.Gate, replayed.Gate);
    }

    private async Task<(LiveTradingStore Store, LiveAccountState Account)> Setup()
    {
        var store = new LiveTradingStore(database.ConnectionString);
        await store.EnsureSchemaAsync();
        var id = new LiveAccountId(Guid.NewGuid());
        var mandate = new LiveMandate(id, 1, "test-key", 1000, 800, 5000, 100, .2m, .8m, 3, .01m, .003m, ["BTC-THB"], null);
        var snapshot = new LiveSnapshot(id, 1, Now, 1000, 0, 0, [], 1000, 0, 0, 0, true, [new("BTC-THB", 99900, 100000, Now)]);
        var account = new LiveAccountState(id, mandate, new(true, false, "test-key", true, 1), snapshot, [], [], [])
        { PerformanceBaseline = new(new(2026, 9, 27), 1000, 1000) };
        await store.CreateAsync(account);
        return (store, account);
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);
    private static DailyMeetingAttempt Report(LiveAccountId id) => new(new(2026, 9, 27), Guid.NewGuid(), "HOLD", null)
    { AccountContext = new(TradingMode.Live, id.Value.ToString("N")) };
}
