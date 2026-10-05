using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class BitkubOnboardingFlowTests(PostgresFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Matching_signed_cash_wallet_provisions_once_without_arming_or_resetting_ledger()
    {
        using var fixture = await Setup();
        Assert.True((await fixture.Provisioner.ProvisionAsync(fixture.Request, default)).Allowed);
        var account = await fixture.Store.ReadAccountAsync(fixture.Id);
        Assert.Equal(1000m, account.Snapshot.CashBalance);
        Assert.Equal(1, account.Mandate.Version);
        Assert.False(account.Gate.Armed);
        Assert.False(account.Snapshot.Reconciled);
        Assert.Null(account.FirstActivatedAt);
        Assert.Null(account.PerformanceBaseline);
        Assert.Empty(account.Orders);
        Assert.Equal(3, fixture.Handler.WalletReads);
        var result = await fixture.Provisioner.ProvisionAsync(fixture.Request, default);
        Assert.Contains("ACCOUNT_ALREADY_PROVISIONED", result.Violations);
        Assert.Equal(3, fixture.Handler.WalletReads);
        Assert.Equal(account.Snapshot.CashBalance, (await fixture.Store.ReadAccountAsync(fixture.Id)).Snapshot.CashBalance);
        Assert.Equal(account.Snapshot.Version, (await fixture.Store.ReadAccountAsync(fixture.Id)).Snapshot.Version);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Changed_wallet_between_signed_observations_cannot_create_an_account()
    {
        using var fixture = await Setup();
        fixture.Handler.ChangeAfterFirstRead = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Provisioner.ProvisionAsync(fixture.Request, default));
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Owner_account_version_and_invalid_limits_are_rejected_before_provider_reads()
    {
        using var fixture = await Setup();
        foreach (var request in new[]
        {
            fixture.Request with { AccountId = new(Guid.NewGuid()) }, fixture.Request with { ExpectedVersion = 1 },
            fixture.Request with { MaximumDailyLoss = 0 }, fixture.Request with { FeeReserveRate = .001m },
            fixture.Request with { AllowedSymbols = ["ETH-THB"] }
        }) Assert.False((await fixture.Provisioner.ProvisionAsync(request, default)).Allowed);
        Assert.Empty(fixture.Handler.Paths);
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
    }

    [Fact]
    public async Task Allocating_a_different_cash_balance_cannot_create_or_arm_an_account()
    {
        using var fixture = await Setup();
        Assert.Contains("CAPITAL_ALLOCATION_MISMATCH", (await fixture.Provisioner.ProvisionAsync(
            fixture.Request with { CapitalAllocation = 999 }, default)).Violations);
        Assert.False(await fixture.Store.ExistsAsync(fixture.Id));
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_activation_requires_baseline_and_matching_wallet_and_credentials(bool rotateCredentials)
    {
        using var fixture = await Setup();
        Assert.True((await fixture.Provisioner.ProvisionAsync(fixture.Request, default)).Allowed);
        var controls = fixture.Services.GetRequiredService<LiveOwnerControls>();
        Assert.False((await controls.PreflightAsync()).Ready);
        var account = await controls.ReadAsync();
        var reconciliation = fixture.Services.GetRequiredService<LiveReconciliation>();
        Assert.True((await reconciliation.InitializeBaselineAsync(new(fixture.Id, 1, account.Snapshot.Version))).Complete);
        account = await controls.ReadAsync();
        Assert.True((await controls.ArmAsync(new(fixture.Id, "synthetic-account", 1, account.Snapshot.Version, true, false))).Allowed);
        if (rotateCredentials) File.WriteAllText(fixture.Credentials, "BITKUB_API_KEY=rotated\nBITKUB_API_SECRET=test-secret\n");
        else fixture.Handler.Cash = 1001;
        Assert.False((await controls.PreflightAsync()).Ready);
        account = await controls.ReadAsync();
        Assert.False(account.Gate.Armed);
        Assert.False(account.Snapshot.Reconciled);
        Assert.Equal(1000, account.Snapshot.CashBalance);
        Assert.Empty(account.Orders);
        Assert.Equal(0, fixture.Handler.OrderWrites);
    }

    [Fact]
    public async Task Configured_missing_account_waits_without_reading_private_broker_or_sending()
    {
        using var fixture = await Setup();
        using var worker = new LiveReconciliationWorker(fixture.Services.GetRequiredService<LiveExecutionService>(),
            fixture.Services.GetRequiredService<LiveReconciliation>(), fixture.Id, fixture.Clock, fixture.Store);
        await worker.StartAsync(default);
        Assert.Equal("ACCOUNT_NOT_PROVISIONED", worker.Status);
        await worker.StopAsync(default);
        Assert.Empty(fixture.Handler.Paths);
    }

    private async Task<Fixture> Setup()
    {
        var fixture = new Fixture(database.ConnectionString);
        await fixture.Store.EnsureSchemaAsync();
        return fixture;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("quantara-onboarding-test-").FullName;
        public LiveAccountId Id { get; } = new(Guid.NewGuid());
        public string Credentials { get; }
        public TestClock Clock { get; } = new();
        public Handler Handler { get; }
        public ServiceProvider Services { get; }
        public LiveTradingStore Store => Services.GetRequiredService<LiveTradingStore>();
        public BitkubAccountProvisioner Provisioner => Services.GetRequiredService<BitkubAccountProvisioner>();
        public LiveMandateUpdate Request => new(Id, 0, 1000, 200, 500, 50, .1m, .2m, 1, .01m, .003m, ["BTC-THB"], Now.AddHours(1));
        public Fixture(string connectionString)
        {
            Credentials = Path.Combine(directory, "credentials.env");
            File.WriteAllText(Credentials, "BITKUB_API_KEY=test-key\nBITKUB_API_SECRET=test-secret\n");
            Handler = new Handler(Clock);
            var broker = new BitkubLiveBroker("test-key", "test-secret", Handler, Clock);
            var verification = new BitkubLiveVerification("synthetic-account", broker.CredentialFingerprint,
                "synthetic-account-confirmation", new string('a', 64), true, true,
                new("synthetic-rules-confirmation", new string('b', 64), .0025m,
                    BitkubBuyHistoryAmount.QuoteIncludingCashFee, [new("BTC-THB", 1, .00000001m, .01m, 10, 0, 1)]));
            var path = Path.Combine(directory, "verification.json");
            File.WriteAllText(path, JsonSerializer.Serialize(verification));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LiveExecution:Enabled"] = "true", ["LiveExecution:AccountId"] = Id.Value.ToString(),
                ["LiveExecution:VerificationFile"] = path, ["Bitkub:CredentialsFile"] = Credentials,
                ["ConnectionStrings:LiveTrading"] = connectionString
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            BitkubLiveRuntime.Configure(services, config);
            services.AddSingleton(broker);
            Services = services.BuildServiceProvider();
        }
        public void Dispose() { Services.Dispose(); Directory.Delete(directory, true); }
    }

    private sealed class TestClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler(TestClock clock) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public int WalletReads { get; private set; }
        public int OrderWrites { get; private set; }
        public bool ChangeAfterFirstRead { get; set; }
        public decimal Cash { get; set; } = 1000;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.EndsWith("/servertime")) return Reply(clock.GetUtcNow().ToUnixTimeMilliseconds().ToString());
            if (path.EndsWith("/balances"))
            {
                WalletReads++;
                var cash = ChangeAfterFirstRead && WalletReads > 1 ? Cash - 1 : Cash;
                return Reply(JsonSerializer.Serialize(new { code = "0", data = new[]
                {
                    new { currency = "THB", available = cash.ToString(System.Globalization.CultureInfo.InvariantCulture), reserved = "0", total = cash.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    new { currency = "BTC", available = "0", reserved = "0", total = "0" },
                    new { currency = "ETH", available = "0", reserved = "0", total = "0" }
                }}));
            }
            if (path.EndsWith("/my-open-orders")) return Reply("{\"error\":0,\"result\":[]}");
            if (path.EndsWith("/my-order-history")) return Reply("{\"error\":0,\"result\":[],\"pagination\":{\"has_next\":false}}");
            if (path.EndsWith("/ticker")) return Reply("[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"99999\",\"lowest_ask\":\"100000\"}]");
            OrderWrites++;
            throw new InvalidOperationException("Unexpected provider operation in onboarding test.");
        }
        private static Task<HttpResponseMessage> Reply(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
