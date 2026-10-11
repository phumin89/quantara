using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;
using Npgsql;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveMeetingReportPersistenceTests(PostgresFixture database)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Meeting_report_survives_fresh_store_restart_and_replay_is_idempotent(bool advisory)
    {
        var (store, account) = await Setup();
        var report = Report(account.AccountId);
        if (advisory) report = report with { Status = "READ_ONLY", Summary = new(0, 0, 0, [])
            { AdvisoryOnly = true, AdvisoryReasons = ["EXECUTION_UNAVAILABLE", "FUNDS_RESERVED"] } };
        await store.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(report, Now); await tx.CommitAsync(ct); return 0; });

        var restarted = new LiveTradingStore(database.ConnectionString);
        var restored = await restarted.ReadAccountAsync(account.AccountId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(report),
            System.Text.Json.JsonSerializer.Serialize(Assert.Single(LiveMeetingReports.Read(restored))));
        Assert.Equal(account.Snapshot.CashBalance, restored.Snapshot.CashBalance);
        Assert.Equal(account.Gate, restored.Gate);
        var revision = await Revision(account.AccountId);

        await restarted.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(report, Now); await tx.CommitAsync(ct); return 0; });
        var replayed = await restarted.ReadAccountAsync(account.AccountId);
        Assert.Single(LiveMeetingReports.Read(replayed));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(restored.Snapshot), System.Text.Json.JsonSerializer.Serialize(replayed.Snapshot));
        Assert.Equal(restored.Gate, replayed.Gate);
        Assert.Equal(revision, await Revision(account.AccountId));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(LiveMeetingReports.Read(replayed)),
            System.Text.Json.JsonSerializer.Serialize(await restarted.ReadMeetingsAsync(account.AccountId)));
    }

    [Fact]
    public async Task Report_projection_remains_account_scoped_and_does_not_wait_for_the_financial_lease()
    {
        var (store, account) = await Setup();
        Assert.Empty(await store.ReadMeetingsAsync(account.AccountId));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadMeetingsAsync(new(Guid.NewGuid())));
        var report = Report(account.AccountId);
        await store.WithAccountLockAsync(account.AccountId, async (tx, ct) =>
        { tx.RecordMeeting(report, Now); await tx.CommitAsync(ct); return 0; });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = store.WithAccountLockAsync(account.AccountId, async (_, ct) =>
        { entered.SetResult(); await release.Task.WaitAsync(ct); return 0; });
        await entered.Task;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.Equal(report, Assert.Single(await store.ReadMeetingsAsync(account.AccountId, deadline.Token)));
            var (_, other) = await Setup();
            Assert.Empty(await store.ReadMeetingsAsync(other.AccountId));
        }
        finally { release.SetResult(); await holder; }
    }

    [Theory]
    [InlineData("AccountId")]
    [InlineData("Snapshot")]
    [InlineData("Mandate")]
    public async Task Report_projection_rejects_a_document_with_another_account_identity(string field)
    {
        var (store, account) = await Setup();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE live.accounts SET document=jsonb_set(document,@path,to_jsonb(@other::text)),revision=revision+1 WHERE id=@id
            """, connection);
        command.Parameters.AddWithValue("path", field == "AccountId" ? new[] { field, "Value" } : new[] { field, "AccountId", "Value" });
        command.Parameters.AddWithValue("other", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("id", account.AccountId.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAccountAsync(account.AccountId));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadMeetingsAsync(account.AccountId));
    }

    private async Task<long> Revision(LiveAccountId id)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT revision FROM live.accounts WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", id.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Same_hour_quarter_review_slots_persist_replay_idempotently_and_reject_conflicts()
    {
        var (store, account) = await Setup();
        var first = Report(account.AccountId) with { ReviewHour = 9, ReviewMinute = 15 };
        var second = Report(account.AccountId) with { ReviewHour = 9, ReviewMinute = 30 };
        await store.WithAccountLockAsync(account.AccountId, async (tx, ct) =>
        {
            tx.RecordMeeting(first, Now.AddHours(2)); tx.RecordMeeting(second, Now.AddHours(2)); await tx.CommitAsync(ct); return 0;
        });
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(account.AccountId);
        Assert.All(LiveMeetingReports.Read(restored), report => Assert.Equal(9, report.ReviewHour));
        Assert.Equal(new[] { 30, 15 }, LiveMeetingReports.Read(restored).Select(x => x.ReviewMinute));
        await store.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(first, Now.AddHours(2)); await tx.CommitAsync(ct); return 0; });
        Assert.Equal(2, LiveMeetingReports.Read(await store.ReadAccountAsync(account.AccountId)).Count);
        var conflict = first with { AttemptId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(conflict, Now); await tx.CommitAsync(ct); return 0; }));
    }

    [Fact]
    public async Task Review_report_rejects_wrong_account_and_future_hour()
    {
        var (store, account) = await Setup();
        var wrong = Report(new LiveAccountId(Guid.NewGuid())) with { ReviewHour = 9 };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(wrong, Now); await tx.CommitAsync(ct); return 0; }));
        var future = Report(account.AccountId) with { ReviewHour = 23 };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(future, Now); await tx.CommitAsync(ct); return 0; }));
        Assert.Empty(LiveMeetingReports.Read(await store.ReadAccountAsync(account.AccountId)));
    }

    [Fact]
    public async Task Research_citations_survive_restart_and_old_report_replay_without_changing_financial_state()
    {
        var (store, account) = await Setup();
        var hash = new string('a', 64);
        var research = new CoinResearchBrief(Now, new string('b', 64),
            [new("DOT-THB", 100, 100.1m, 500000, -2, .1m, 10000, false, false)],
            [new("doc-" + hash, "BTC-THB", "Bitcoin whitepaper", "https://bitcoin.org/bitcoin.pdf", hash, Now)],
            [new("BTC-THB", "ข้อมูลจากโครงการ ยังไม่มีการยืนยันจากแหล่งอิสระ",
                [new("ข้อเสนอเครือข่าย", "PROJECT_CLAIM", "doc-" + hash, "A purely peer-to-peer version of electronic cash")])],
            ["ข้อมูลทดสอบเท่านั้น"]);
        var report = Report(account.AccountId) with { Summary = new(0, 0, 0, []) { Research = research } };
        await store.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(report, Now); await tx.CommitAsync(ct); return 0; });
        var restarted = new LiveTradingStore(database.ConnectionString);
        await restarted.WithAccountLockAsync(account.AccountId, async (tx, ct) => { tx.RecordMeeting(report, Now.AddDays(10)); await tx.CommitAsync(ct); return 0; });
        var restored = await restarted.ReadAccountAsync(account.AccountId);
        var saved = Assert.Single(LiveMeetingReports.Read(restored)).Summary!.Research!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(research), System.Text.Json.JsonSerializer.Serialize(saved));
        Assert.Equal(account.Gate, restored.Gate);
        Assert.Equal(account.Mandate.Version, restored.Mandate.Version);
        Assert.Empty(restored.Orders); Assert.Empty(restored.Fills);
        var malicious = report with { AttemptId = Guid.NewGuid(), Summary = report.Summary! with
        { Research = research with { Sources = [research.Sources[0] with { Url = "https://localhost:7443/private" }] } } };
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.WithAccountLockAsync(account.AccountId,
            async (tx, ct) => { tx.RecordMeeting(malicious, Now); await tx.CommitAsync(ct); return 0; }));
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
