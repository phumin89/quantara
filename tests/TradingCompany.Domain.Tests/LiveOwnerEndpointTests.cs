using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveOwnerEndpointTests
{
    [Theory]
    [InlineData("expired", "MANDATE_EXPIRED")]
    [InlineData("isolation", "ISOLATION_REQUIRED")]
    [InlineData("credentials", "ACCOUNT_BINDING_CHANGED")]
    [InlineData("baseline", "DAILY_BASELINE_REQUIRED")]
    [InlineData("unknown-order", "UNRESOLVED_ORDER")]
    public async Task Saved_armed_flag_does_not_report_trading_ready_after_a_gate_fails(string failure, string reason)
    {
        await using var factory = new Factory();
        using var client = await factory.Owner();
        var account = factory.Store.Account with { Gate = LiveRiskTests.Gate };
        factory.Store.Account = failure switch
        {
            "expired" => account with { Mandate = account.Mandate with { ExpiresAt = LiveRiskTests.Now } },
            "isolation" => account with { Gate = account.Gate with { IsolationVerified = false } },
            "baseline" => account with { PerformanceBaseline = account.PerformanceBaseline! with { BangkokDate = new(2026, 9, 26) } },
            "unknown-order" => account with { Orders = [new(LiveRiskTests.Intent, LiveOrderState.Unknown, null,
                401.2m, 0, 0, "test", LiveRiskTests.Now, [])] },
            _ => account with { Gate = account.Gate with { CredentialFingerprint = "changed-key" } }
        };
        var status = await client.GetFromJsonAsync<JsonElement>("/live/status");
        Assert.Equal("BLOCKED", status.GetProperty("state").GetString());
        Assert.False(status.GetProperty("armed").GetBoolean());
        Assert.Contains(reason, status.GetProperty("reasons").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Cancellation_resolves_only_a_saved_intent_and_keeps_reserves_until_reconciliation()
    {
        await using var factory = new Factory();
        using var client = await factory.Owner();
        var tx = new LiveAccountTransaction(factory.Store.Account with { Gate = LiveRiskTests.Gate }, (_, _) => Task.CompletedTask);
        tx.Reserve(LiveRiskTests.Intent, LiveRiskTests.Now);
        tx.RecordTransition(LiveRiskTests.Intent.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        tx.RecordTransition(LiveRiskTests.Intent.Id, LiveOrderState.Acknowledged, "saved-broker-order", LiveRiskTests.Now);
        tx.SetStopped(true);
        factory.Store.Account = tx.Account;
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsync($"/live/orders/{Guid.NewGuid()}/cancel", null)).StatusCode);
        Assert.Equal(0, factory.Broker.CancelCalls);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/live/orders/{LiveRiskTests.Intent.Id}/cancel", null)).StatusCode);
        Assert.Equal("saved-broker-order", factory.Broker.CancelledId);
        Assert.Equal(401.2m, factory.Store.Account.Snapshot.CashReserved);
        Assert.Equal(LiveOrderState.Acknowledged, Assert.Single(factory.Store.Account.Orders).State);
        Assert.True(factory.Store.Account.Gate.Stopped);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/live/orders/{LiveRiskTests.Intent.Id}/cancel", null)).StatusCode);
        Assert.Equal(1, factory.Broker.CancelCalls);
    }

    [Theory]
    [InlineData("/live/arm")]
    [InlineData("/live/stop")]
    [InlineData("/live/mandate")]
    [InlineData("/live/preflight")]
    [InlineData("/live/baseline")]
    [InlineData("/live/provision")]
    [InlineData("/live/day-baseline")]
    [InlineData("/live/reconcile")]
    [InlineData("/live/orders/33333333-3333-3333-3333-333333333333/cancel")]
    public async Task Anonymous_cannot_call_owner_controls(string path)
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        Assert.Empty(factory.Store.Account.Audit);
    }

    [Fact]
    public async Task Owner_initializes_baseline_from_two_broker_reads_without_arming()
    {
        await using var factory = new Factory();
        factory.Store.Account = factory.Store.Account with { PerformanceBaseline = null };
        using var client = await factory.Owner();
        var request = new LiveBaselineRequest(LiveRiskTests.Account, 1, 1);
        var result = await client.PostAsJsonAsync("/live/baseline", request);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, factory.Evidence.Reads);
        Assert.Equal(1000m, factory.Store.Account.PerformanceBaseline!.DayStartEquity);
        Assert.False(factory.Store.Account.Gate.Armed);
        Assert.Single(factory.Store.Account.Audit, a => a.Kind == "InitialPerformanceBaselineEstablished");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/live/baseline",
            request with { SnapshotVersion = factory.Store.Account.Snapshot.Version })).StatusCode);
        Assert.Equal(0, factory.Broker.CancelCalls);
    }

    [Fact]
    public async Task Baseline_initialization_rejects_cross_account_and_missing_csrf_before_broker_reads()
    {
        await using var factory = new Factory();
        factory.Store.Account = factory.Store.Account with { PerformanceBaseline = null };
        using var client = await factory.Owner();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/live/baseline",
            new LiveBaselineRequest(new(Guid.NewGuid()), 1, 1))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/live/baseline",
            new LiveBaselineRequest(LiveRiskTests.Account, 1, 1))).StatusCode);
        Assert.Equal(0, factory.Evidence.Reads);
        Assert.Null(factory.Store.Account.PerformanceBaseline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_broker_or_lost_account_lease_cannot_persist_initial_baseline(bool lostLease)
    {
        await using var factory = new Factory();
        factory.Store.Account = factory.Store.Account with { PerformanceBaseline = null };
        factory.Store.FailLease = lostLease;
        factory.Evidence.Fail = !lostLease;
        using var client = await factory.Owner();
        var result = await client.PostAsJsonAsync("/live/baseline", new LiveBaselineRequest(LiveRiskTests.Account, 1, 1));
        Assert.Equal(lostLease ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Conflict, result.StatusCode);
        Assert.DoesNotContain("private-source-secret", await result.Content.ReadAsStringAsync());
        Assert.Null(factory.Store.Account.PerformanceBaseline);
        Assert.Equal(1000m, factory.Store.Account.Snapshot.CashBalance);
        Assert.DoesNotContain(factory.Store.Account.Audit, a => a.Kind == "InitialPerformanceBaselineEstablished");
    }

    [Fact]
    public async Task Read_preflight_arm_and_stop_follow_real_saved_state_without_exposing_private_evidence()
    {
        await using var factory = new Factory();
        using var client = await factory.Owner();
        var account = await client.GetFromJsonAsync<JsonElement>("/live/account");
        Assert.Equal(1000, account.GetProperty("snapshot").GetProperty("cashBalance").GetDecimal());
        Assert.False(account.TryGetProperty("audit", out _));
        Assert.False(account.TryGetProperty("fills", out _));
        Assert.DoesNotContain("key-fingerprint", account.GetRawText());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/live/preflight", null)).StatusCode);
        Assert.False(factory.Store.Account.Gate.Armed);
        Assert.Empty(factory.Store.Account.Audit);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/live/arm", ArmRequest())).StatusCode);
        Assert.True(factory.Store.Account.Gate.Armed);
        Assert.Equal("ARMED", (await client.GetFromJsonAsync<JsonElement>("/live/status")).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/live/stop", null)).StatusCode);
        Assert.True(factory.Store.Account.Gate.Stopped);
        Assert.False(factory.Store.Account.Gate.Armed);
        Assert.Equal("STOPPED", (await client.GetFromJsonAsync<JsonElement>("/live/status")).GetProperty("state").GetString());
        Assert.Empty(factory.Store.Account.Orders);
    }

    [Fact]
    public async Task Missing_csrf_cannot_stop_or_arm_or_change_limits()
    {
        await using var factory = new Factory();
        using var client = await factory.Owner();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        foreach (var path in new[] { "/live/arm", "/live/stop", "/live/mandate", "/live/preflight" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        Assert.Empty(factory.Store.Account.Audit);
    }

    [Fact]
    public async Task Saving_owner_limits_disarms_and_rejects_a_stale_repeat()
    {
        await using var factory = new Factory();
        using var client = await factory.Owner();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/live/arm", ArmRequest())).StatusCode);
        var update = new
        {
            accountId = LiveRiskTests.Account, expectedVersion = 1, capitalAllocation = 1000m,
            maximumOrderNotional = 300m, maximumDailyTurnover = 1000m, maximumDailyLoss = 50m,
            maximumDrawdown = .1m, maximumPortfolioExposure = .8m, maximumOpenPositions = 2,
            maximumSlippage = .01m, feeReserveRate = .003m, allowedSymbols = new[] { "BTC-THB" }
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/live/mandate", update)).StatusCode);
        Assert.Equal(300m, factory.Store.Account.Mandate.MaximumOrderNotional);
        Assert.Equal(2, factory.Store.Account.Mandate.Version);
        Assert.False(factory.Store.Account.Gate.Armed);
        Assert.False(factory.Store.Account.Snapshot.Reconciled);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/live/mandate", update)).StatusCode);
        Assert.Equal(2, factory.Store.Account.Mandate.Version);
    }

    [Fact]
    public async Task Backend_failure_is_unavailable_not_zero_balances_or_a_private_error()
    {
        await using var factory = new Factory();
        using var client = await factory.Owner();
        factory.Store.Fail = true;
        foreach (var path in new[] { "/live/account", "/live/status" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("LIVE_UNAVAILABLE", text);
            Assert.DoesNotContain("private-database-secret", text);
            Assert.DoesNotContain("cashBalance", text);
        }
    }

    [Fact]
    public async Task Owner_meetings_supports_date_filtering_and_preserves_history_index()
    {
        await using var factory = new Factory();
        await SeedMeetings(factory);
        using var client = await factory.Owner();

        var before = factory.Store.Account;
        factory.Source.Fail = true;
        factory.Evidence.Fail = true;

        var all = await client.GetFromJsonAsync<JsonElement>("/live/meetings");
        Assert.Equal(31, all.GetProperty("meetings").GetArrayLength());
        Assert.Equal(new[] { "2026-09-26", "2026-09-25", "2026-09-23" },
            all.GetProperty("availableDates").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Null(all.GetProperty("selectedDate").GetString());

        var old = await client.GetFromJsonAsync<JsonElement>("/live/meetings?date=2026-09-23");
        Assert.Equal(1, old.GetProperty("meetings").GetArrayLength());
        Assert.Equal("2026-09-23", old.GetProperty("selectedDate").GetString());

        var busy = await client.GetFromJsonAsync<JsonElement>("/live/meetings?date=2026-09-25");
        Assert.Equal(25, busy.GetProperty("meetings").GetArrayLength());
        Assert.All(busy.GetProperty("meetings").EnumerateArray(), meeting =>
            Assert.Equal("2026-09-25", meeting.GetProperty("businessDate").GetString()));

        var empty = await client.GetFromJsonAsync<JsonElement>("/live/meetings?date=2026-09-24");
        Assert.Empty(empty.GetProperty("meetings").EnumerateArray());
        Assert.Equal("2026-09-24", empty.GetProperty("selectedDate").GetString());
        Assert.Equal(before.Snapshot, factory.Store.Account.Snapshot);
        Assert.Equal(before.Gate, factory.Store.Account.Gate);
        Assert.Equal(before.Mandate, factory.Store.Account.Mandate);
        Assert.Equal(0, factory.Evidence.Reads);
        Assert.Equal(0, factory.Broker.CancelCalls);
    }

    [Theory]
    [InlineData("2026-9-27")]
    [InlineData("2026-02-29")]
    [InlineData("")]
    [InlineData("2026-09-23&date=2026-09-25")]
    [InlineData("2026-09-28")]
    public async Task Owner_meetings_rejects_invalid_or_future_dates(string date)
    {
        await using var factory = new Factory();
        using var client = await factory.Owner();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/live/meetings?date={date}")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_read_meetings()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/live/meetings")).StatusCode);
    }

    private static async Task SeedMeetings(Factory factory)
    {
        var tx = new LiveAccountTransaction(factory.Store.Account, (state, _) =>
        {
            factory.Store.Account = state;
            return Task.CompletedTask;
        });
        tx.RecordMeeting(Meeting(new(2026, 9, 23), null), LiveRiskTests.Now);
        tx.RecordMeeting(Meeting(new(2026, 9, 25), null), LiveRiskTests.Now);
        foreach (var hour in Enumerable.Range(0, 24))
            tx.RecordMeeting(Meeting(new(2026, 9, 25), hour), LiveRiskTests.Now);
        foreach (var hour in Enumerable.Range(0, 7))
            tx.RecordMeeting(Meeting(new(2026, 9, 26), hour), LiveRiskTests.Now);
        await tx.CommitAsync(CancellationToken.None);
    }

    private static DailyMeetingAttempt Meeting(DateOnly date, int? hour) => new(date, Guid.NewGuid(), "HOLD", ReviewHour: hour)
    {
        AccountContext = new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N"))
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bad_preflight_disarms_and_returns_a_fixed_safe_reason(bool returnsNull)
    {
        await using var factory = new Factory();
        using var client = await factory.Owner();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/live/arm", ArmRequest())).StatusCode);
        factory.Source.Fail = !returnsNull;
        factory.Source.Null = returnsNull;
        var result = await client.PostAsJsonAsync("/live/arm", ArmRequest());
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Contains("PREFLIGHT_UNAVAILABLE", await result.Content.ReadAsStringAsync());
        Assert.False(factory.Store.Account.Gate.Armed);
        Assert.DoesNotContain(factory.Store.Account.Audit, e => e.Detail.Contains("private-broker-secret"));
    }

    private static LiveArmRequest ArmRequest() => new(LiveRiskTests.Account, "bound-broker-account", 1, 1, true, false);

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => LiveRiskTests.Now; }
    private sealed class Source : ILiveActivationSource
    {
        public bool Fail { get; set; }
        public bool Null { get; set; }
        public Task<LiveActivationEvidence> CheckAsync(LiveAccountState account, CancellationToken cancellationToken)
        {
            if (Fail) throw new IOException("private-broker-secret");
            return Task.FromResult(Null ? null! : new LiveActivationEvidence(account.AccountId, "bound-broker-account",
                account.Mandate.CredentialFingerprint, account.Mandate.Version, account.Snapshot.Version,
                LiveRiskTests.Now, true, true, true, true));
        }
    }
    private sealed class EvidenceSource : ILiveReconciliationSource
    {
        public int Reads { get; private set; }
        public bool Fail { get; set; }
        public Task<LiveBrokerEvidence> ReadAsync(LiveAccountId id, CancellationToken ct)
        {
            Reads++;
            if (Fail) throw new IOException("private-source-secret");
            return Task.FromResult(new LiveBrokerEvidence(LiveRiskTests.Account, "key-fingerprint", LiveRiskTests.Now,
                true, 1000m, 0m, [], [], [], LiveRiskTests.Snapshot.Quotes));
        }
    }
    private sealed class Store : ILiveTradingStore
    {
        public LiveAccountState Account { get; set; } = new(LiveRiskTests.Account, LiveRiskTests.Mandate,
            LiveRiskTests.Gate with { Armed = false }, LiveRiskTests.Snapshot, [], [], [])
        { PerformanceBaseline = new(new(2026, 9, 27), 1000, 1000) };
        public bool Fail { get; set; }
        public bool FailLease { get; set; }
        private readonly SemaphoreSlim gate = new(1);
        public Task<LiveSnapshot> ReadAsync(LiveAccountId id, CancellationToken cancellationToken) => Task.FromResult(Account.Snapshot);
        public Task<LiveAccountState> ReadAccountAsync(LiveAccountId id, CancellationToken cancellationToken)
            => Fail ? throw new IOException("private-database-secret") : Task.FromResult(Account);
        public async Task<T> WithAccountLockAsync<T>(LiveAccountId id, Func<ILiveAccountTransaction, CancellationToken, Task<T>> action, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                if (Fail) throw new IOException("private-database-secret");
                return await action(new LiveAccountTransaction(Account, (state, _) => { Account = state; return Task.CompletedTask; },
                    _ => FailLease ? throw new IOException("lost-lease") : Task.CompletedTask), ct);
            }
            finally { gate.Release(); }
        }
    }
    private sealed class Broker : ILiveBroker
    {
        public int CancelCalls { get; private set; }
        public string? CancelledId { get; private set; }
        public Task<PreparedLiveOrder> PrepareAsync(LiveIntent intent, CancellationToken ct) => throw new InvalidOperationException("No placement allowed in owner routes.");
        public Task<BrokerSubmission> PlaceAsync(PreparedLiveOrder request, CancellationToken ct) => throw new InvalidOperationException("No placement allowed in owner routes.");
        public Task<BrokerCancellation> CancelAsync(string symbol, string orderId, OrderSide side, CancellationToken ct)
        {
            Assert.Equal("BTC-THB", symbol);
            Assert.Equal(OrderSide.Buy, side);
            CancelCalls++;
            CancelledId = orderId;
            return Task.FromResult(new BrokerCancellation(BrokerSubmissionState.Accepted, "cancel-accepted"));
        }
    }
    private sealed class Factory : WebApplicationFactory<LiveProgram>
    {
        public Store Store { get; } = new();
        public Source Source { get; } = new();
        public Broker Broker { get; } = new();
        public EvidenceSource Evidence { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new Clock());
                services.AddSingleton(new LiveOwnerControls(Store, Source, new Clock(), LiveRiskTests.Account));
                services.AddSingleton(new LiveExecutionService(Store, Broker, new Clock()));
                services.AddSingleton(new LiveReconciliation(Store, Evidence, new Clock()));
            });
        }
        public HttpClient Client()
        {
            var client = CreateClient(new() { BaseAddress = new("https://localhost:7443"), AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("Origin", "https://localhost:3000");
            return client;
        }
        public async Task<HttpClient> Owner()
        {
            var client = Client();
            var code = Services.GetRequiredService<OwnerSession>().IssuePairingCode();
            await Token(client);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/owner/pair", new { code })).StatusCode);
            await Token(client);
            return client;
        }
        private static async Task Token(HttpClient client)
        {
            var token = await client.GetFromJsonAsync<JsonElement>("/owner/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        }
    }
}
