using System.Text.Json;
using TradingCompany.AgentRuntime;
using TradingCompany.Application;
using TradingCompany.Api;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveCompanyCoordinatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-live-company-" + Guid.NewGuid().ToString("N"));
    private static TradingAccountContext Scope => new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N"));
    private readonly Clock clock = new();

    [Fact]
    public async Task Expert_meeting_uses_owned_live_money_and_dispatches_once_after_nine()
    {
        var client = new Client();
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        clock.Now = LiveRiskTests.Now.AddMinutes(-1); // 08:59 Bangkok
        await coordinator.TickAsync();
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Intents);
        clock.Now = LiveRiskTests.Now;
        await coordinator.TickAsync();
        var intent = Assert.Single(client.Intents);
        Assert.Equal(TradingMode.Live, intent.Mode);
        Assert.Equal(LiveRiskTests.Account, intent.AccountId);
        Assert.Equal(1, intent.MandateVersion);
        Assert.Equal(1, intent.SnapshotVersion);
        Assert.Equal("BTC-THB", intent.Symbol);
        Assert.Equal(.0015m, intent.Quantity); // 15% of actual 1000 THB, ask 100000
        Assert.Equal(150m, intent.BuyBudget);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(Scope, input.AccountContext);
            Assert.Equal(1000m, input.Portfolio.Equity);
            Assert.Equal(1000m, input.Portfolio.Cash);
            Assert.Single(input.Candidates);
        });
        var restarted = Coordinator(client, gateway);
        await restarted.TickAsync();
        Assert.Single(client.Intents);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.Equal("ALREADY_ATTEMPTED", restarted.Status);
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("invalid-output")]
    [InlineData("model-down")]
    [InlineData("stale-version")]
    [InlineData("stop-during-meeting")]
    [InlineData("quote-expired")]
    public async Task Missing_authority_or_invalid_advice_never_dispatches(string scenario)
    {
        var client = new Client();
        var gateway = new Gateway { Scenario = scenario };
        gateway.AfterFinal = () =>
        {
            if (scenario == "stale-version") client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 2 } };
            if (scenario == "stop-during-meeting") client.Packet = client.Packet with { Stopped = true };
            if (scenario == "quote-expired") clock.Now = clock.Now.AddSeconds(31);
        };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Empty(client.Intents);
        Assert.NotEqual("DISABLED", coordinator.Status);
    }

    [Theory]
    [InlineData("disarmed")]
    [InlineData("stopped")]
    [InlineData("unreconciled")]
    [InlineData("wrong-account")]
    [InlineData("stale")]
    [InlineData("zero-budget")]
    [InlineData("invalid-equity")]
    [InlineData("negative-loss")]
    [InlineData("negative-pending")]
    [InlineData("duplicate-position")]
    [InlineData("duplicate-quote")]
    public async Task Invalid_live_state_blocks_research_and_model_quota(string scenario)
    {
        var client = new Client();
        client.Packet = scenario switch
        {
            "disarmed" => client.Packet with { Armed = false },
            "stopped" => client.Packet with { Stopped = true },
            "unreconciled" => client.Packet with { Snapshot = client.Packet.Snapshot with { Reconciled = false } },
            "wrong-account" => client.Packet with { AccountContext = Scope with { AccountId = Guid.NewGuid().ToString("N") } },
            "zero-budget" => client.Packet with { Limits = client.Packet.Limits with { CapitalAllocation = 0 } },
            "invalid-equity" => client.Packet with { Snapshot = client.Packet.Snapshot with { Equity = 1100m } },
            "negative-loss" => client.Packet with { Snapshot = client.Packet.Snapshot with { DailyLoss = -1m } },
            "negative-pending" => client.Packet with { Snapshot = client.Packet.Snapshot with { PendingBuyNotional = -1m } },
            "duplicate-position" => client.Packet with { Snapshot = client.Packet.Snapshot with
                { Positions = [new("BTC-THB", .001m, 0, 100000m), new("BTC-THB", .001m, 0, 100000m)], Equity = 1200m } },
            "duplicate-quote" => client.Packet with { Snapshot = client.Packet.Snapshot with
                { Quotes = [.. client.Packet.Snapshot.Quotes, client.Packet.Snapshot.Quotes[0] with { Ask = 0 }] } },
            _ => client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now.AddSeconds(-31) } }
        };
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("LIVE_BLOCKED", coordinator.Status);
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Intents);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("synthetic")]
    [InlineData("missing")]
    public async Task Incomplete_or_non_market_evidence_never_reaches_agents(string scenario)
    {
        var client = new Client();
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway, new Market { Scenario = scenario });
        await coordinator.TickAsync();
        Assert.Equal("RESEARCH_UNAVAILABLE", coordinator.Status);
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Intents);
    }

    private LiveCompanyCoordinator Coordinator(Client client, Gateway gateway, Market? market = null)
    {
        var runner = new AiCompanyMeetingRunner(gateway,
            new JsonAgentDecisionArtifactStore(Path.Combine(root, "artifacts")),
            new(true, "test-model", "medium", "company-v1", "live-v1", 600, 8, 20000, TimeSpan.FromSeconds(30)));
        return new(client, market ?? new Market(), runner,
            new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope), clock, LiveRiskTests.Account);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = LiveRiskTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Client : ILiveCompanyClient
    {
        public LiveResearchPacket Packet { get; set; } = new(Scope, 1, LiveRiskTests.Snapshot,
            new(1000m, .2m, 3, .003m, .01m, ["BTC-THB"]), true, false);
        public List<LiveIntent> Intents { get; } = [];
        public Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken) => Task.FromResult(Packet);
        public Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            return Task.FromResult(new LiveIntentReceipt(intent, LiveOrderState.Acknowledged, []));
        }
    }
    internal sealed class Gateway : ITradingAgentGateway
    {
        public string Scenario { get; init; } = "buy";
        public Action? AfterFinal { get; set; }
        public List<AiCompanyMeetingInput> Inputs { get; } = [];
        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken)
        {
            if (Scenario == "model-down") throw new AgentModelUnavailableException("fixture");
            using var document = JsonDocument.Parse(request.InputJson);
            var input = document.RootElement.GetProperty("meeting").Deserialize<AiCompanyMeetingInput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Inputs.Add(input);
            if (request.Role == "portfolio-final") AfterFinal?.Invoke();
            var output = JsonSerializer.Serialize(new
            {
                role = request.Role, summary = "Review completed market bars", recommendations = new[] { "Review evidence" },
                risks = new[] { "Price changes" }, evidenceIds = input.Candidates.Select(c => c.EvidenceId),
                dataCutoff = input.DataCutoff.ToString("yyyy-MM-dd"), evidenceHash = input.EvidenceHash,
                proposals = request.Role != "portfolio-final" || Scenario == "hold" ? [] : new[]
                {
                    new { action = "BUY", symbol = Scenario == "invalid-output" ? "INVENTED" : "BTC-THB", targetWeight = .15m,
                        confidence = .8m, thesis = "Supplied evidence supports entry", invalidation = "Trend deteriorates" }
                }
            });
            return Task.FromResult(new AgentModelResponse(output, "test-response", request.Model, 100, 50, TimeSpan.Zero));
        }
    }
    internal sealed class Market : IHistoricalMarketEvidenceProvider
    {
        public string Scenario { get; init; } = "normal";
        public Task<IReadOnlyList<HistoricalBar>> GetAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<HistoricalMarketEvidence> GetEvidenceAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
        {
            var count = Scenario == "missing" ? 10 : 250;
            var bars = Enumerable.Range(0, count).Select(i => new HistoricalBar(end.AddDays(i - count + 1), symbol,
                75000m + i, 76000m + i, 74000m + i, 75500m + i, 1000)).ToArray();
            if (Scenario == "future") bars[^1] = bars[^1] with { Date = end.AddDays(1) };
            return Task.FromResult(new HistoricalMarketEvidence(bars, bars, [], Scenario == "synthetic" ? "synthetic-demo" : "bitkub-daily", "THB"));
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
