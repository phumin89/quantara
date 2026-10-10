using System.Text.Json;
using TradingCompany.AgentRuntime;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CompanyLearningServiceTests
{
    private static readonly DateOnly Day = new(2026, 10, 7);
    private static readonly TradingAccountContext Account = new(TradingMode.Live, "11111111111111111111111111111111");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Eight_roles_learn_once_then_reuse_durable_memory_and_review_again_next_day()
    {
        using var f = new Fixture(); f.History();
        f.Clock.Now = At(Day, 8);
        Assert.Null(await f.Service().RefreshAsync(default));
        f.Clock.Now = At(Day, 9);
        var first = await f.Service().RefreshAsync(default);
        Assert.NotNull(first);
        Assert.Equal(CompanyLearningValidation.Roles, first.Roles.Select(r => r.Role));
        Assert.All(first.Roles, r => Assert.Equal("ACCEPTED", r.Status));
        Assert.Equal(1, f.Gateway.Calls);
        Assert.Equal(JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(await f.Service().RefreshAsync(default), Json));
        Assert.NotNull(await f.Service().ReadAsync(default));
        f.History(Day, 10); // Later reports do not invalidate earlier genuine citations.
        Assert.NotNull(await f.Service().ReadAsync(default));
        f.Clock.Now = At(Day.AddDays(1), 9);
        Assert.All((await f.Service().RefreshAsync(default))!.Roles, r => Assert.Equal("ACCEPTED", r.Status));
        Assert.Equal(2, f.Gateway.Calls);
        var artifacts = Directory.GetFiles(Path.Combine(f.Root, "artifacts"), "*.json").Select(file => JsonSerializer.Deserialize<AgentDecisionArtifact>(File.ReadAllText(file), Json)!).ToArray();
        Assert.Equal(2, artifacts.Count(a => a.Key.Role == "company-learning-output"));
        Assert.Equal(200, artifacts.Sum(a => a.InputTokens)); // Draft and validated projection cannot double-count quota.
        Assert.All(f.Gateway.Requests, r => { Assert.Equal(4096, r.MaxOutputTokens); Assert.Contains("not demonstrated performance", r.SystemInstructions); });
        using var schema = JsonDocument.Parse(f.Gateway.Requests[0].JsonSchema);
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        using var context = JsonDocument.Parse(f.Gateway.Requests[0].InputJson);
        Assert.True(context.RootElement.TryGetProperty("sharedCritic", out _));
        Assert.All(context.RootElement.GetProperty("history").EnumerateArray(), h => Assert.False(h.TryGetProperty("critic", out _)));
    }

    [Fact]
    public async Task Missing_own_history_is_explicit_and_no_history_does_not_spend_quota()
    {
        using var f = new Fixture();
        Assert.All((await f.Service().RefreshAsync(default))!.Roles, r => Assert.Equal("NO_HISTORY", r.Status));
        Assert.Null(await f.Service().ReadAsync(default)); Assert.Equal(0, f.Gateway.Calls);
        f.Clock.Now = At(Day.AddDays(1), 9); f.History(roles: ["ceo", "risk-critic"]);
        var report = await f.Service().RefreshAsync(default);
        Assert.Equal(2, report!.Roles.Count(r => r.Status == "ACCEPTED"));
        Assert.Equal(6, report.Roles.Count(r => r.Status == "NO_HISTORY"));
    }

    [Fact]
    public async Task Shared_critic_uses_latest_completed_quarter_within_same_review_hour()
    {
        using var f = new Fixture();
        f.History(Day.AddDays(-1), 9);
        f.HistoryAt(Day.AddDays(-1), 9, 15);
        f.HistoryAt(Day.AddDays(-1), 9, 45);
        f.StartedHistoryAt(Day.AddDays(-1), 10, 0);
        f.Clock.Now = At(Day, 9);

        var report = await f.Service().RefreshAsync(default);

        Assert.All(report!.Roles, role => Assert.Equal("ACCEPTED", role.Status));
        Assert.Equal(CompanyLearningValidation.Roles, report.Roles.Select(role => role.Role));
        Assert.All(report.Roles, role =>
        {
            var source = f.ReportAt(Day.AddDays(-1), 9, 45, role.Role);
            Assert.Equal(source.ArtifactId, role.Lesson!.ArtifactId);
            Assert.Equal(source.Summary[..40], role.Lesson.Quote);
        });
        using var context = JsonDocument.Parse(f.Gateway.Requests.Single().InputJson);
        Assert.Equal(f.CriticArtifactIdAt(Day.AddDays(-1), 9, 45),
            context.RootElement.GetProperty("sharedCritic").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("quote-pair")]
    [InlineData("other-role")]
    [InlineData("fabricated")]
    [InlineData("duplicate")]
    [InlineData("unknown-field")]
    [InlineData("oversize")]
    public async Task Invalid_citations_are_rejected_per_role_and_malformed_envelopes_as_a_whole(string defect)
    {
        using var f = new Fixture(); f.History(); f.Gateway.Defect = defect;
        var report = await f.Service().RefreshAsync(default);
        if (defect is "quote-pair" or "other-role" or "fabricated")
        {
            Assert.Equal("INVALID", report!.Roles[0].Status); Assert.Null(report.Roles[0].Lesson);
            Assert.Equal(7, report.Roles.Count(r => r.Status == "ACCEPTED"));
            Assert.NotNull(await f.Service().ReadAsync(default));
        }
        else { Assert.All(report!.Roles, r => Assert.Equal("INVALID", r.Status)); Assert.Null(await f.Service().ReadAsync(default)); }
        await f.Service().RefreshAsync(default); Assert.Equal(1, f.Gateway.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_or_cancellation_keeps_attempt_marker_across_restart(bool cancellation)
    {
        using var f = new Fixture(); f.History(); using var cts = new CancellationTokenSource();
        f.Gateway.Fail = () => { if (cancellation) { cts.Cancel(); throw new OperationCanceledException(cts.Token); } throw new IOException("Synthetic provider failure"); };
        if (cancellation) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service().RefreshAsync(cts.Token));
        else Assert.All((await f.Service().RefreshAsync(default))!.Roles, r => Assert.Equal("INVALID", r.Status));
        Assert.All((await f.Service().RefreshAsync(default))!.Roles, r => Assert.Equal("INVALID", r.Status));
        Assert.Equal(1, f.Gateway.Calls); Assert.Null(await f.Service().ReadAsync(default));
        f.Clock.Now = At(Day.AddDays(1), 9); f.Gateway.Fail = null;
        Assert.NotNull(await f.Service().RefreshAsync(default)); Assert.Equal(2, f.Gateway.Calls);
    }

    [Fact]
    public async Task Account_and_age_boundaries_prevent_memory_leakage()
    {
        using var f = new Fixture(); f.History(); await f.Service().RefreshAsync(default);
        var other = new JsonDailyMeetingJournal(Path.Combine(f.Root, "other.json"), new(TradingMode.Live, "22222222222222222222222222222222"));
        Assert.Null(await f.Service(other).ReadAsync(default));
        var sandbox = new JsonDailyMeetingJournal(Path.Combine(f.Root, "paper.json"));
        Assert.Null(await f.Service(sandbox).RefreshAsync(default));
        f.Clock.Now = At(Day.AddDays(-1), 9); Assert.Null(await f.Service().ReadAsync(default));
        f.Clock.Now = At(Day.AddDays(8), 9); Assert.Null(await f.Service().ReadAsync(default));
        Assert.Equal(1, f.Gateway.Calls);
    }

    [Fact]
    public void Journal_rejects_learning_from_wrong_account_and_preserves_prior_state()
    {
        using var f = new Fixture(); var id = Guid.NewGuid(); f.Journal.TryBegin(Day, id);
        var wrong = new CompanyLearningReport(Day, new(TradingMode.Live, "22222222222222222222222222222222"),
            CompanyLearningValidation.Roles.Select(r => new CompanyRoleLearning(r, "NO_HISTORY", 0, null)).ToArray(), 0);
        Assert.Throws<InvalidDataException>(() => f.Journal.Complete(Day, id, "HOLD", new(0, 0, 0, []) { Learning = wrong }));
        Assert.Equal("STARTED", f.Journal.Read(Day)!.Status);
    }

    private static DateTimeOffset At(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(7));
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = At(Day, 11); public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime(); }
    private sealed class Gateway : ITradingAgentGateway
    {
        public int Calls; public string? Defect; public Action? Fail; public List<AgentModelRequest> Requests = [];
        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken token)
        {
            Calls++; Requests.Add(request); Fail?.Invoke();
            using var doc = JsonDocument.Parse(request.InputJson);
            var roles = doc.RootElement.GetProperty("history").EnumerateArray().Select(h =>
            {
                var role = h.GetProperty("role").GetString()!; var own = h.GetProperty("own");
                return own.ValueKind == JsonValueKind.Null ? new CompanyRoleLearning(role, "NO_HISTORY", 0, null) :
                    new CompanyRoleLearning(role, "ACCEPTED", 1, new(own.GetProperty("id").GetString()!, own.GetProperty("summary").GetString()![..40], "ตรวจข้อสรุปกับข้อมูลใหม่", "ทดสอบสมมติฐานก่อนเสนอคำสั่ง"));
            }).ToArray();
            if (Defect == "quote-pair") roles[0] = roles[0] with { Lesson = roles[0].Lesson! with { ArtifactId = roles[6].Lesson!.ArtifactId } };
            if (Defect == "other-role") roles[0] = roles[0] with { Lesson = roles[1].Lesson };
            if (Defect == "fabricated") roles[0] = roles[0] with { Lesson = roles[0].Lesson! with { Quote = "This quote was never in any saved report." } };
            if (Defect == "duplicate") roles[1] = roles[0];
            var output = Defect == "unknown-field" ? JsonSerializer.Serialize(new { roles, secret = "ignored?" }, Json) : JsonSerializer.Serialize(new { roles }, Json);
            if (Defect == "oversize") output += new string(' ', 20000);
            return Task.FromResult(new AgentModelResponse(output, "synthetic", "fixture", 100, 100, TimeSpan.Zero));
        }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("quantara-learning-test-").FullName;
        public readonly Clock Clock = new(); public readonly Gateway Gateway = new();
        public JsonDailyMeetingJournal Journal => new(Path.Combine(Root, "meetings.json"), Account);
        private readonly List<JsonAgentDecisionArtifactStore> stores = [];
        public CompanyLearningService Service(IDailyMeetingJournal? journal = null)
        {
            var store = new JsonAgentDecisionArtifactStore(Path.Combine(Root, "artifacts")); stores.Add(store);
            return new(Gateway, store, journal ?? Journal, "fixture", Clock);
        }
        public void History(DateOnly? day = null, int? hour = null, string[]? roles = null)
        {
            var d = day ?? Day.AddDays(-1); var id = Guid.NewGuid();
            var reports = (roles ?? CompanyLearningValidation.Roles.ToArray()).Select(r => new SavedAiReport(r,
                $"{r}: distinct historical reasoning for the saved meeting with reproducible evidence.", "fixture", 1, 1, Guid.NewGuid().ToString(), new string('a', 64), d)).ToArray();
            if (hour is { } h) Journal.TryBegin(d, id, h, null); else Journal.TryBegin(d, id);
            Journal.Complete(d, id, "HOLD", new(reports.Length, reports.Length, reports.Length, reports));
        }
        public void HistoryAt(DateOnly day, int hour, int minute)
        {
            var id = Guid.NewGuid();
            var reports = CompanyLearningValidation.Roles.Select(r => new SavedAiReport(r,
                $"{r}: distinct historical reasoning for the saved meeting with reproducible evidence.", "fixture", 1, 1,
                Guid.NewGuid().ToString(), new string('a', 64), day)).ToArray();
            Journal.TryBegin(day, id, hour, null, minute);
            Journal.Complete(day, id, "HOLD", new(reports.Length, reports.Length, reports.Length, reports));
        }
        public void StartedHistoryAt(DateOnly day, int hour, int minute)
            => Journal.TryBegin(day, Guid.NewGuid(), hour, null, minute);
        public SavedAiReport ReportAt(DateOnly day, int hour, int minute, string role) => Journal.ReadAll()
            .Single(a => a.BusinessDate == day && a.ReviewHour == hour && a.ReviewMinute == minute)
            .Summary!.Reports.Single(r => r.Role == role);
        public string CriticArtifactIdAt(DateOnly day, int hour, int minute) => ReportAt(day, hour, minute, "risk-critic").ArtifactId;
        public void Dispose() { foreach (var store in stores) store.Dispose(); Directory.Delete(Root, true); }
    }
}
