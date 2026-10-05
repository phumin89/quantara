using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingCompany.Api;
using TradingCompany.Application;
using TradingCompany.AgentRuntime;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class ForwardPaperCompanyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-company-rehearsal-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new();
    private readonly ScriptedGateway gateway = new();
    private readonly Quotes quotes = new();
    private JsonForwardPaperStore Paper => new(Path.Combine(root, "paper.json"));
    private JsonDailyMeetingJournal Journal => new(Path.Combine(root, "meetings.json"));
    private ForwardPaperCompanyService Service(bool enabled = true, decimal initialCapital = 0m)
    {
        var funding = new JsonForwardFundingStore(Path.Combine(root, "funding.json"));
        if (funding.Read() is null) funding.Advance(DateTimeOffset.Parse("2026-09-09T12:00:00Z"));
        return new(funding, new JsonForwardInvestmentStore(Path.Combine(root, "legacy.json")), Paper,
            new AiCompanyMeetingRunner(gateway, new JsonAgentDecisionArtifactStore(Path.Combine(root, "artifacts")),
                new(enabled, "scripted-test-only", "medium", "test-v1", "paper-test", 600, 8, 20000, TimeSpan.FromSeconds(30))),
            new Bars(), quotes, Journal, clock,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardFunding:Enabled"] = "true",
                ["ForwardFunding:StatePath"] = Path.Combine(root, "funding.json"),
                ["ForwardFunding:InitialPaperCapital"] = initialCapital.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Build(),
            NullLogger<ForwardPaperCompanyService>.Instance);
    }

    [Fact]
    public async Task Persisted_pause_blocks_new_work_and_resume_does_not_repeat_a_completed_meeting()
    {
        var service = Service();
        Assert.True(service.SetPaused(true));
        await service.TickAsync();
        Assert.Equal("PAUSED", service.GetStatus().Status);
        Assert.Equal(0, gateway.Calls);
        var restarted = Service();
        Assert.True(restarted.GetStatus().Paused);
        Assert.True(restarted.SetPaused(false));
        await restarted.TickAsync();
        Assert.Equal(8, gateway.Calls);
        Assert.True(restarted.SetPaused(true));
        var before = File.ReadAllBytes(Path.Combine(root, "paper.json"));
        clock.Now = clock.Now.AddMinutes(2);
        await restarted.TickAsync();
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "paper.json")));
        Assert.True(restarted.SetPaused(false));
        Assert.False(restarted.GetStatus().Paused);
        Assert.NotEqual("PAUSED", restarted.GetStatus().Status);
        await restarted.TickAsync();
        Assert.Equal(8, gateway.Calls);
    }

    [Fact]
    public void Resume_cannot_clear_an_interrupted_meeting()
    {
        var service = Service();
        Assert.True(service.SetPaused(true));
        Journal.TryBegin(ForwardFundingCalendar.BangkokDate(clock.Now), Guid.NewGuid());
        Assert.False(service.SetPaused(false));
        Assert.True(service.GetStatus().Paused);
        Assert.Equal("RECONCILIATION_REQUIRED", service.GetStatus().Status);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Initial_capital_runs_meeting_when_salary_investment_is_still_paused()
    {
        clock.Now = DateTimeOffset.Parse("2026-09-30T02:00:00Z");
        await Service(initialCapital: 100000m).TickAsync();
        Assert.Equal(8, gateway.Calls);
        Assert.Equal(100000m, ForwardPaperLedger.Snapshot(Paper.Read()!).Cash);
        Assert.Single(ForwardPaperLedger.Snapshot(Paper.Read()!).PendingOrders);
        Assert.Equal(0m, new JsonForwardFundingStore(Path.Combine(root, "funding.json")).Read()!.InvestmentCash);
        await Service(initialCapital: 100000m).TickAsync();
        Assert.Equal(8, gateway.Calls);
        Assert.Equal(100000m, ForwardPaperLedger.Snapshot(Paper.Read()!).TotalContributed);
    }

    [Fact]
    public async Task Completed_meeting_summary_survives_service_reconstruction_and_status_read()
    {
        await Service().TickAsync();
        var attempt = Journal.Read(new(2026, 11, 30));
        Assert.NotNull(attempt?.Summary);
        Assert.Equal(8, attempt!.Summary!.Reports.Count);
        var status = Service().GetStatus();
        Assert.Equal(new(2026, 11, 30), status.LastMeetingDate);
        Assert.Equal(new(2026, 11, 30), status.LastMeeting!.BusinessDate);
    }

    [Fact]
    public async Task Failed_meeting_preserves_usage_without_reports()
    {
        gateway.Invalid = true;
        await Service().TickAsync();
        var summary = Journal.Read(new(2026, 11, 30))!.Summary;
        Assert.NotNull(summary);
        Assert.True(summary!.InputTokens > 0);
        Assert.Empty(summary.Reports);
    }

    [Fact]
    public void Status_serialization_preserves_existing_lowercase_fields()
    {
        var json = JsonSerializer.Serialize(Service().GetStatus());
        Assert.Contains("\"status\"", json);
        Assert.Contains("\"modelEnabled\"", json);
        Assert.Contains("\"lastMeetingDate\"", json);
    }

    [Fact]
    public async Task Real_orchestration_with_scripted_gateway_funds_buys_holds_sells_and_reconciles_across_restart()
    {
        var service = Service();
        await service.TickAsync();
        var pending = ForwardPaperLedger.Snapshot(Paper.Read()!);
        Assert.Equal(8295m, pending.Cash);
        Assert.Single(pending.PendingOrders);
        Assert.Empty(pending.Receipts);
        Assert.Equal(8, gateway.Calls);
        clock.Now = clock.Now.AddMinutes(2);
        await Service().TickAsync();
        var bought = ForwardPaperLedger.Snapshot(Paper.Read()!);
        Assert.Single(bought.Receipts);
        Assert.Single(bought.Positions);
        Assert.Equal(7235m, bought.Cash);
        Assert.Equal(8, gateway.Calls);
        gateway.Action = "HOLD";
        clock.Now = DateTimeOffset.Parse("2026-12-01T02:00:00Z");
        await Service().TickAsync();
        Assert.Empty(ForwardPaperLedger.Snapshot(Paper.Read()!).PendingOrders);
        Assert.Equal(16, gateway.Calls);
        gateway.Action = "SELL"; quotes.Price = 33m;
        clock.Now = DateTimeOffset.Parse("2026-12-02T02:00:00Z");
        await Service().TickAsync();
        clock.Now = clock.Now.AddMinutes(2);
        await Service().TickAsync();
        var sold = ForwardPaperLedger.Snapshot(Paper.Read()!);
        Assert.Empty(sold.Positions);
        Assert.Equal(8380m, sold.Cash);
        Assert.Equal(85m, sold.TotalPnl);
        Assert.Equal(85m, sold.RealizedPnl);
        Assert.Equal(2, sold.Receipts.Count);
        Assert.Equal(24, gateway.Calls);
    }

    [Fact]
    public async Task Disabled_model_never_calls_gateway_or_creates_orders()
    {
        await Service(false).TickAsync();
        Assert.Equal(0, gateway.Calls);
        Assert.Empty(ForwardPaperLedger.Snapshot(Paper.Read()!).PendingOrders);
    }

    [Fact]
    public async Task Interrupted_prior_day_prevents_pending_execution_after_restart()
    {
        await Service().TickAsync();
        clock.Now = DateTimeOffset.Parse("2026-12-01T02:00:00Z");
        Assert.True(Journal.TryBegin(new(2026, 12, 1), Guid.NewGuid()));
        clock.Now = clock.Now.AddDays(1);
        var before = File.ReadAllBytes(Path.Combine(root, "paper.json"));
        await Service().TickAsync();
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "paper.json")));
        Assert.Empty(ForwardPaperLedger.Snapshot(Paper.Read()!).Receipts);
    }

    [Fact]
    public async Task Invalid_final_response_creates_no_order()
    {
        gateway.Invalid = true;
        await Service().TickAsync();
        Assert.Empty(ForwardPaperLedger.Snapshot(Paper.Read()!).PendingOrders);
        Assert.Equal("INVALID_FINAL", Journal.Read(new(2026, 11, 30))!.Status);
    }

    [Fact]
    public async Task Weekend_runs_crypto_meeting_once_without_equity_candidates()
    {
        clock.Now = DateTimeOffset.Parse("2026-12-05T02:00:00Z");
        quotes.Symbols = ["NVDA", "BTC-THB"];
        gateway.Action = "HOLD";
        gateway.Symbol = "BTC-THB";
        await Service().TickAsync();
        await Service().TickAsync();
        Assert.Equal(8, gateway.Calls);
        Assert.Equal(new[] { "BTC-THB" }, gateway.LastSymbols);
        Assert.Empty(ForwardPaperLedger.Snapshot(Paper.Read()!).PendingOrders);
    }

    [Fact]
    public async Task Weekend_equity_only_profile_does_not_call_model()
    {
        clock.Now = DateTimeOffset.Parse("2026-12-05T02:00:00Z");
        await Service().TickAsync();
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Hold_meeting_preserves_reports_without_creating_a_trade()
    {
        gateway.Action = "HOLD";
        await Service().TickAsync();
        var reconstructed = Service().GetStatus();
        Assert.Equal("COMPLETED", reconstructed.Status);
        Assert.Equal(8, reconstructed.LastMeeting!.Summary!.Reports.Count);
        Assert.Empty(ForwardPaperLedger.Snapshot(Paper.Read()!).PendingOrders);
        Assert.Empty(ForwardPaperLedger.Snapshot(Paper.Read()!).Receipts);
        Assert.Equal(8, gateway.Calls);
    }

    [Fact]
    public async Task Prior_day_reports_are_not_presented_as_today_after_restart()
    {
        await Service().TickAsync();
        clock.Now = clock.Now.AddDays(1);
        var before = File.ReadAllBytes(Path.Combine(root, "paper.json"));
        Assert.Null(Service().GetStatus().LastMeeting);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "paper.json")));
        Assert.Equal(8, gateway.Calls);
    }

    [Fact]
    public async Task Corrupt_meeting_status_is_read_only_and_requires_reconciliation()
    {
        await Service().TickAsync();
        var before = File.ReadAllBytes(Path.Combine(root, "paper.json"));
        File.WriteAllText(Path.Combine(root, "meetings.json"), "invalid journal");
        var service = Service();
        Assert.Equal("RECONCILIATION_REQUIRED", service.GetStatus().Status);
        Assert.Null(service.GetStatus().LastMeeting);
        Assert.Equal("invalid journal", File.ReadAllText(Path.Combine(root, "meetings.json")));
        await service.TickAsync();
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "paper.json")));
        Assert.Equal(8, gateway.Calls);
    }

    [Fact]
    public async Task Active_meeting_is_working_but_interrupted_claim_is_blocked()
    {
        var service = Service();
        gateway.DuringCall = () => Assert.Equal("MEETING", service.GetStatus().Status);
        await service.TickAsync();
        gateway.DuringCall = null;
        clock.Now = clock.Now.AddDays(1);
        Journal.TryBegin(ForwardFundingCalendar.BangkokDate(clock.Now), Guid.NewGuid());
        Assert.Equal("RECONCILIATION_REQUIRED", Service().GetStatus().Status);
    }

    [Fact]
    public void Old_completed_journal_without_summary_does_not_invent_reports()
    {
        var service = Service();
        var id = Guid.NewGuid();
        var day = ForwardFundingCalendar.BangkokDate(clock.Now);
        Assert.True(Journal.TryBegin(day, id));
        Journal.Complete(day, id, "COMPLETED");
        var saved = service.GetStatus().LastMeeting;
        Assert.NotNull(saved);
        Assert.Null(saved.Summary);
        Assert.Equal(0, gateway.Calls);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-30T02:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Quotes : IForwardPaperQuotes
    {
        public decimal Price = 30m;
        public string[] Symbols = ["NVDA"];
        public IReadOnlyList<string> SupportedSymbols => Symbols;
        public Task<ForwardPaperQuote?> GetAsync(string symbol, DateTimeOffset now, CancellationToken cancellationToken)
            => Task.FromResult<ForwardPaperQuote?>(new(symbol, Price, "USD", 35, now.AddSeconds(-1), now,
                now.AddHours(-1), "scripted-rehearsal-only", 10m));
    }
    private sealed class Bars : IHistoricalMarketDataProvider
    {
        public Task<IReadOnlyList<HistoricalBar>> GetAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<HistoricalBar>>(Enumerable.Range(0, 250)
                .Select(i => new HistoricalBar(end.AddDays(i - 249), symbol, 1050, 1050, 1050, 1050, 10000)).ToArray());
    }
    private sealed class ScriptedGateway : ITradingAgentGateway
    {
        public int Calls; public string Action = "BUY"; public bool Invalid;
        public Action? DuringCall;
        public string Symbol = "NVDA";
        public string[] LastSymbols = [];
        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            DuringCall?.Invoke();
            using var json = JsonDocument.Parse(request.InputJson);
            var input = json.RootElement.GetProperty("meeting");
            LastSymbols = input.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("symbol").GetProperty("value").GetString()!).ToArray();
            var proposals = request.Role == "portfolio-final"
                ? new[] { new { action = Action, symbol = Invalid ? "INVENTED" : Symbol, targetWeight = .19m, confidence = .8m,
                    thesis = "Scripted test, not investment research", invalidation = "Test evidence changes" } } : [];
            var output = JsonSerializer.Serialize(new { role = request.Role, summary = "Scripted offline rehearsal", recommendations = new[] { "Fixture only" },
                risks = new[] { "Not a real AI decision" }, evidenceIds = new[] { input.GetProperty("candidates")[0].GetProperty("evidenceId").GetString() },
                proposals, dataCutoff = input.GetProperty("dataCutoff").GetString(), evidenceHash = input.GetProperty("evidenceHash").GetString() });
            return Task.FromResult(new AgentModelResponse(output, Guid.NewGuid().ToString(), request.Model, 100, 50, TimeSpan.Zero));
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
