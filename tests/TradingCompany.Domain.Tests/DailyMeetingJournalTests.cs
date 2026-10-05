using TradingCompany.Infrastructure;
using TradingCompany.Application;
using System.Text.Json.Serialization;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class DailyMeetingJournalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-meeting-test-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(root, "meetings.json");
    private static readonly DateOnly Day = new(2026, 9, 12);
    private JsonDailyMeetingJournal Store() => new(StatePath);

    [Fact]
    public void Restart_does_not_repeat_completed_day()
    {
        var id = Guid.NewGuid();
        Assert.True(Store().TryBegin(Day, id));
        Store().Complete(Day, id, "COMPLETED");
        Assert.False(Store().TryBegin(Day, Guid.NewGuid()));
        Assert.Equal("COMPLETED", Store().Read(Day)!.Status);
        Assert.True(Store().TryBegin(Day.AddDays(1), Guid.NewGuid()));
    }

    [Fact]
    public void Crash_after_claim_blocks_today_and_later_days()
    {
        Assert.True(Store().TryBegin(Day, Guid.NewGuid()));
        Assert.False(Store().TryBegin(Day, Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => Store().TryBegin(Day.AddDays(1), Guid.NewGuid()));
        Assert.Equal("STARTED", Store().Read(Day)!.Status);
    }

    [Fact]
    public void Provider_failure_consumes_day_without_automatic_retry()
    {
        var id = Guid.NewGuid();
        Store().TryBegin(Day, id);
        Store().Complete(Day, id, "UNAVAILABLE");
        Assert.False(Store().TryBegin(Day, Guid.NewGuid()));
        Assert.Equal("UNAVAILABLE", Store().Read(Day)!.Status);
    }

    [Fact]
    public async Task Concurrent_instances_claim_day_once()
    {
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Store().TryBegin(Day, Guid.NewGuid()))));
        Assert.Single(claims, claim => claim);
    }

    [Fact]
    public void Wrong_owner_cannot_finish_claim()
    {
        Store().TryBegin(Day, Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => Store().Complete(Day, Guid.NewGuid(), "COMPLETED"));
        Assert.Equal("STARTED", Store().Read(Day)!.Status);
    }

    [Fact]
    public void Missing_previously_created_state_is_not_reset()
    {
        Store().TryBegin(Day, Guid.NewGuid());
        File.Delete(StatePath);
        Assert.Throws<InvalidDataException>(() => Store().TryBegin(Day, Guid.NewGuid()));
    }

    [Fact]
    public void Corrupt_state_blocks_claim_without_overwriting_evidence()
    {
        Store().TryBegin(Day, Guid.NewGuid());
        using (var file = new FileStream(StatePath, FileMode.Open, FileAccess.Write)) file.SetLength(0);
        Assert.Throws<InvalidDataException>(() => Store().TryBegin(Day.AddDays(1), Guid.NewGuid()));
        Assert.Equal(0, new FileInfo(StatePath).Length);
    }

    [Fact]
    public void Status_read_has_no_creation_side_effect()
    {
        Assert.Null(Store().Read(Day));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Clock_rollback_cannot_create_an_earlier_day()
    {
        var id = Guid.NewGuid(); Store().TryBegin(Day, id); Store().Complete(Day, id, "COMPLETED");
        Assert.Throws<InvalidOperationException>(() => Store().TryBegin(Day.AddDays(-1), Guid.NewGuid()));
    }

    [Fact]
    public void Old_format_without_summary_reads_with_null_summary()
    {
        var attempt = new DailyMeetingAttempt(Day, Guid.NewGuid(), "COMPLETED");
        var json = System.Text.Json.JsonSerializer.Serialize(new { hash = Hash([attempt]), attempts = new[] { attempt } },
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Directory.CreateDirectory(root);
        File.WriteAllText(StatePath, json);

        var result = Store().Read(Day);

        Assert.NotNull(result);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void Summary_survives_restart_and_tampering_is_rejected()
    {
        var id = Guid.NewGuid();
        var report = new SavedAiReport("Analyst", "summary", "model", 3, 5, "artifact", "evidence", Day);
        var summary = new DailyMeetingSummary(1, 3, 5, [report]);
        Store().TryBegin(Day, id);
        Store().Complete(Day, id, "COMPLETED", summary);

        var result = new JsonDailyMeetingJournal(StatePath).Read(Day);
        Assert.Equal(summary.ModelCalls, result!.Summary!.ModelCalls);
        Assert.Equal(summary.InputTokens, result.Summary.InputTokens);
        Assert.Equal(summary.OutputTokens, result.Summary.OutputTokens);
        Assert.Equal(summary.Reports, result.Summary.Reports);
        var content = File.ReadAllText(StatePath).Replace("summary", "tampered");
        File.WriteAllText(StatePath, content);
        Assert.Throws<InvalidDataException>(() => Store().Read(Day));
    }

    private static string Hash(DailyMeetingAttempt[] attempts) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(attempts,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull })));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
