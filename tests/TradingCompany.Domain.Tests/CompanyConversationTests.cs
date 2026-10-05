using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Npgsql;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CompanyConversationTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(7));
    private static AgentDecisionArtifact Artifact(Guid? id = null, string summary = "Hold cash") => new(id ?? Guid.NewGuid(),
        new("model", "v1", "portfolio-final", "hash", "mandate", new(2026, 9, 30)), At,
        JsonSerializer.Serialize(new { role = "portfolio-final", summary, recommendations = new[] { "Wait for evidence" }, risks = new[] { "No validation" },
            proposals = new[] { new { action = "HOLD", symbol = "BTC-THB", targetWeight = 0m, confidence = 0m, thesis = "No edge", invalidation = "Validated edge" } } }), "request", "model", 1, 1, TimeSpan.Zero);

    [Fact]
    public void Legacy_report_is_not_promoted_by_a_completed_label()
    {
        var artifact = Artifact();
        var message = CompanyConversationProjection.Project(artifact, new(new(2026, 10, 1), Guid.NewGuid(), "COMPLETED"));
        Assert.Equal("saved-report", message.EvidenceStatus);
        Assert.Equal("HOLD", Assert.Single(message.Proposals).Action);
    }

    [Fact]
    public void Matching_accepted_artifact_can_be_reused_in_a_later_business_round()
    {
        var artifact = Artifact();
        var report = new SavedAiReport(artifact.Key.Role, "Hold cash", "model", 1, 1, artifact.ArtifactId.ToString(), artifact.Key.EvidenceHash, artifact.Key.DataCutoff);
        var meeting = new DailyMeetingAttempt(new(2026, 10, 2), Guid.NewGuid(), "COMPLETED", new(0, 0, 0, [report]));
        Assert.Equal("accepted", CompanyConversationProjection.Project(artifact, meeting).EvidenceStatus);
        Assert.Equal("saved-report", CompanyConversationProjection.Project(artifact, meeting with { Status = "INVALID_FINAL" }).EvidenceStatus);
        Assert.Equal("saved-report", CompanyConversationProjection.Project(artifact, meeting with { Summary = new(0, 0, 0, [report with { EvidenceHash = "other" }]) }).EvidenceStatus);
    }

    [Fact]
    public void Bangkok_date_is_used_at_UTC_day_boundary()
    {
        var artifact = Artifact();
        var boundary = new AgentDecisionArtifact(artifact.ArtifactId, artifact.Key, new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero), artifact.OutputJson,
            "request", "model", 1, 1, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 10, 2), CompanyConversationProjection.BusinessDate(boundary));
    }

    [Fact]
    public void Wrong_speaker_cannot_be_projected_into_the_conversation()
    {
        var artifact = Artifact();
        var wrong = new AgentDecisionArtifact(artifact.ArtifactId, artifact.Key, At, "{\"role\":\"ceo\",\"summary\":\"wrong\"}", "request", "model", 1, 1, TimeSpan.Zero);
        Assert.Throws<InvalidDataException>(() => CompanyConversationProjection.Project(wrong, null));
    }

    [ReportDatabaseFact]
    public async Task PostgreSQL_persists_reports_without_duplicates_and_rolls_back_conflicts()
    {
        // Each run owns a disposable database; never alter the owner's report/account database.
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("REPORT_DB_CONFIG")!));
        var builder = new NpgsqlConnectionStringBuilder(settings.RootElement.GetProperty("connectionString").GetString());
        builder.Host = "report-db"; builder.Port = 5432; builder.Pooling = false;
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        var database = "report_test_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            builder.Database = database;
            var store = new PostgresCompanyConversationStore(builder.ConnectionString);
            var message = CompanyConversationProjection.Project(Artifact(), null);
            var day = new CompanyConversation(new(2026, 10, 1), "COMPLETED", [message], At.ToUniversalTime());
            await store.ImportAsync([day], default);
            await store.ImportAsync([day], default);
            Assert.Single((await store.ReadAsync(day.BusinessDate, default)).Messages);
            var restarted = new PostgresCompanyConversationStore(builder.ConnectionString);
            Assert.Equal(message.Summary, Assert.Single((await restarted.ReadAsync(day.BusinessDate, default)).Messages).Summary);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync([day with { Status = "BAD", Messages = [message with { Summary = "tampered" }] }], default));
            Assert.Equal("COMPLETED", (await store.ReadAsync(day.BusinessDate, default)).Status);
            await store.ImportAsync([day with { BusinessDate = new(2026, 10, 2), Messages = [message with { EvidenceStatus = "accepted" }] }], default);
            Assert.Equal(2, (await store.DatesAsync(default)).Dates.Count);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData(40000, "paper-execution", "สำเร็จ")]
    [InlineData(12000, "risk-engine", "ปฏิเสธ")]
    public void Only_validated_ledger_events_prove_execution_or_risk_rejection(int capital, string role, string result)
    {
        var account = ForwardPaperLedger.Fund(ForwardPaperLedger.Open(Guid.NewGuid()), new(new(2026, 10, 1), capital, At));
        var order = new ForwardPaperIntent(Guid.NewGuid(), "NVDA", TradingCompany.Domain.OrderSide.Buy, 2, At.AddMinutes(1), new());
        account = ForwardPaperLedger.Submit(account, order, order.DecisionAt);
        var pending = Assert.Single(CompanyConversationProjection.PaperMessages(account)).Message;
        Assert.Contains("ยังไม่ใช่ผลซื้อขาย", pending.Summary);
        var quote = new ForwardPaperQuote("NVDA", 100, "USD", 35, At.AddMinutes(2), At.AddMinutes(2), At, "controlled-fixture", 10);
        account = ForwardPaperLedger.Execute(account, order.Id, quote, At.AddMinutes(3));
        var projected = CompanyConversationProjection.PaperMessages(account);
        Assert.Equal(2, projected.Count);
        Assert.Equal(role, projected[1].Message.Role);
        Assert.Contains(result, projected[1].Message.Summary);
        Assert.Equal(projected[1].Message.Id, CompanyConversationProjection.PaperMessages(account)[1].Message.Id);
    }
}

public sealed class ReportDatabaseFactAttribute : FactAttribute
{
    public ReportDatabaseFactAttribute() { if (Environment.GetEnvironmentVariable("REPORT_DB_CONFIG") is null) Skip = "Set REPORT_DB_CONFIG in the sandbox Docker network for PostgreSQL proof."; }
}
