using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.LiveExecution;
using TradingCompany.AgentRuntime;
using TradingCompany.Api;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveCoordinatorAccessTests
{
    private const string Token = "test-coordinator-token-012345678901234567890";

    [Fact]
    public async Task Reserved_unsent_order_blocks_new_advisory_readiness()
    {
        await using var factory = new Factory();
        var tx = new LiveAccountTransaction(factory.Store.Account, (_, _) => Task.CompletedTask);
        tx.Reserve(LiveRiskTests.Intent, LiveRiskTests.Now);
        factory.Store.Account = tx.Account;
        using var client = factory.Client();
        var packet = await client.GetFromJsonAsync<LiveResearchPacket>($"/internal/live/research/{LiveRiskTests.Account.Value:N}");
        Assert.False(packet!.Armed);
        Assert.Equal(LiveOrderState.Reserved, Assert.Single(factory.Store.Account.Orders).State);
        Assert.Equal(0, factory.Broker.PlaceCalls);
    }

    [Fact]
    public async Task Specialist_company_reaches_executor_over_scoped_http_without_paper_funding_or_duplicate_send()
    {
        await using var factory = new Factory();
        // This fixture supplies one native pair and one fresh quote; do not claim that
        // missing ETH/SOL quotes are usable merely because the account is armed.
        factory.Store.Account = factory.Store.Account with
            { Mandate = factory.Store.Account.Mandate with { AllowedSymbols = ["BTC-THB"] } };
        var root = Path.Combine(Path.GetTempPath(), "quantara-live-flow-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var transport = new HttpClient(factory.Server.CreateHandler());
            var client = new LiveExecutionClient(transport, Token, LiveRiskTests.Account);
            var gateway = new LiveCompanyCoordinatorTests.Gateway();
            var runner = new AiCompanyMeetingRunner(gateway, new JsonAgentDecisionArtifactStore(Path.Combine(root, "artifacts")),
                new(true, "test-model", "medium", "company-v1", "live-v1", 600, 8, 20000, TimeSpan.FromSeconds(30)));
            var scope = new TradingAccountContext(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N"));
            var path = Path.Combine(root, "meetings.json");
            var coordinator = new LiveCompanyCoordinator(client, new LiveCompanyCoordinatorTests.Market(), runner,
                new JsonDailyMeetingJournal(path, scope), new Clock(), LiveRiskTests.Account);
            await coordinator.TickAsync();
            Assert.Equal("ORDER_PENDING", coordinator.Status);
            var order = Assert.Single(factory.Store.Account.Orders);
            Assert.Equal(LiveOrderState.Acknowledged, order.State);
            Assert.Equal(150m, order.Intent.BuyBudget);
            Assert.Equal(150.45m, factory.Store.Account.Snapshot.CashReserved);
            Assert.Equal(1000m, factory.Store.Account.Snapshot.CashBalance);
            Assert.Empty(factory.Store.Account.Fills);
            Assert.Equal(1, factory.Broker.PlaceCalls);
            Assert.Equal(8, gateway.Inputs.Count);
            var restarted = new LiveCompanyCoordinator(client, new LiveCompanyCoordinatorTests.Market(), runner,
                new JsonDailyMeetingJournal(path, scope), new Clock(), LiveRiskTests.Account);
            await restarted.TickAsync();
            Assert.Equal("ALREADY_ATTEMPTED", restarted.Status);
            Assert.Equal(1, factory.Broker.PlaceCalls);
            Assert.Equal(8, gateway.Inputs.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("invalid-token-012345678901234567890\n")]
    public async Task Missing_or_malformed_server_token_is_denied_not_an_internal_error(string? configuredToken)
    {
        await using var factory = new Factory { ConfiguredToken = configuredToken };
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/internal/live/research/{LiveRiskTests.Account.Value:N}")).StatusCode);
        Assert.Empty(factory.Store.Account.Orders);
    }

    [Fact]
    public async Task Scoped_coordinator_can_read_redacted_research_and_submit_through_real_risk_boundary()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var response = await client.GetAsync($"/internal/live/research/{LiveRiskTests.Account.Value:N}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("key-fingerprint", text);
        Assert.DoesNotContain(Token, text);
        var packet = JsonSerializer.Deserialize<LiveResearchPacket>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(packet.Armed);
        Assert.Equal(1000m, packet.Snapshot.CashBalance);
        Assert.Equal(LiveRiskTests.Account.Value.ToString("N"), packet.AccountContext.AccountId);
        var intent = LiveRiskTests.Intent with { Quantity = .0015m, BuyBudget = 150m };
        var order = await client.PostAsJsonAsync("/internal/live/intents", intent);
        Assert.Equal(HttpStatusCode.OK, order.StatusCode);
        Assert.Equal(LiveOrderState.Acknowledged, Assert.Single(factory.Store.Account.Orders).State);
        Assert.Equal(150.45m, factory.Store.Account.Snapshot.CashReserved);
        Assert.Equal(1000m, factory.Store.Account.Snapshot.CashBalance);
        Assert.Empty(factory.Store.Account.Fills);
        Assert.DoesNotContain("key-fingerprint", await order.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("wrong-token", HttpStatusCode.Unauthorized)]
    [InlineData("browser-origin", HttpStatusCode.Forbidden)]
    [InlineData("other-account", HttpStatusCode.Forbidden)]
    public async Task Untrusted_call_cannot_read_a_live_account(string scenario, HttpStatusCode status)
    {
        await using var factory = new Factory();
        using var client = factory.Client(scenario == "wrong-token" ? "incorrect" : Token);
        if (scenario == "browser-origin") client.DefaultRequestHeaders.Add("Origin", "https://localhost:3000");
        var id = scenario == "other-account" ? Guid.NewGuid() : LiveRiskTests.Account.Value;
        Assert.Equal(status, (await client.GetAsync($"/internal/live/research/{id:N}")).StatusCode);
        Assert.Empty(factory.Store.Account.Orders);
    }

    [Fact]
    public async Task Coordinator_token_cannot_use_owner_controls_or_submit_paper_or_other_account_intents()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/live/account")).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "https://localhost:3000");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/live/arm", new LiveArmRequest(
            LiveRiskTests.Account, "test", 1, 1, true, false))).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        foreach (var intent in new[] { LiveRiskTests.Intent with { Mode = TradingMode.Sandbox },
            LiveRiskTests.Intent with { AccountId = new(Guid.NewGuid()) } })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/internal/live/intents", intent)).StatusCode);
        Assert.Empty(factory.Store.Account.Orders);
    }

    [Fact]
    public async Task Executor_stop_blocks_advice_even_with_a_previously_valid_token()
    {
        await using var factory = new Factory();
        factory.Store.Account = factory.Store.Account with { Gate = LiveRiskTests.Gate with { Stopped = true } };
        using var client = factory.Client();
        var packet = await client.GetFromJsonAsync<LiveResearchPacket>($"/internal/live/research/{LiveRiskTests.Account.Value:N}");
        Assert.False(packet!.Armed);
        var response = await client.PostAsJsonAsync("/internal/live/intents", LiveRiskTests.Intent with { BuyBudget = 400m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = Assert.Single(factory.Store.Account.Orders);
        Assert.Equal(LiveOrderState.Rejected, order.State);
        Assert.Contains("STOPPED", order.Violations);
    }

    private sealed class Factory : WebApplicationFactory<LiveProgram>
    {
        public Store Store { get; } = new();
        public Broker Broker { get; } = new();
        public string? ConfiguredToken { get; init; } = Token;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new Clock());
                services.Configure<CoordinatorAccessOptions>(CoordinatorAccess.SchemeName, o => { o.Token = ConfiguredToken!; o.AccountId = LiveRiskTests.Account; });
                services.AddSingleton<ILiveTradingStore>(Store);
                services.AddSingleton(new LiveOwnerControls(Store, new NoPreflight(), new Clock(), LiveRiskTests.Account));
                services.AddSingleton(new LiveExecutionService(Store, Broker, new Clock()));
            });
        }
        public HttpClient Client(string token = Token)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost:7443"), AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            return client;
        }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => LiveRiskTests.Now; }
    private sealed class NoPreflight : ILiveActivationSource
    { public Task<LiveActivationEvidence> CheckAsync(LiveAccountState account, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
    private sealed class Store : ILiveTradingStore
    {
        public LiveAccountState Account { get; set; } = new(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot, [], [], []) { PerformanceBaseline = new(new(2026, 9, 27), 1000, 1000) };
        public Task<LiveAccountState> ReadAccountAsync(LiveAccountId id, CancellationToken cancellationToken = default) => Task.FromResult(Account);
        public Task<LiveSnapshot> ReadAsync(LiveAccountId id, CancellationToken cancellationToken = default) => Task.FromResult(Account.Snapshot);
        public Task<T> WithAccountLockAsync<T>(LiveAccountId id, Func<ILiveAccountTransaction, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
            => action(new LiveAccountTransaction(Account, (state, _) => { Account = state; return Task.CompletedTask; }, _ => Task.CompletedTask), cancellationToken);
    }
    private sealed class Broker : ILiveBroker
    {
        public int PlaceCalls { get; private set; }
        public Task<PreparedLiveOrder> PrepareAsync(LiveIntent intent, CancellationToken cancellationToken)
        {
            const string body = "test-only-body";
            return Task.FromResult(new PreparedLiveOrder(intent, body, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))));
        }
        public Task<BrokerSubmission> PlaceAsync(PreparedLiveOrder request, CancellationToken cancellationToken)
        {
            PlaceCalls++;
            return Task.FromResult(new BrokerSubmission(BrokerSubmissionState.Accepted, "test-order", "ACCEPTED"));
        }
        public Task<BrokerCancellation> CancelAsync(string symbol, string orderId, OrderSide side, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
