using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubActivationPreflightTests
{
    private static readonly string Fingerprint = "sha256:" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes("test-key\0test-secret")));

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task Verified_initial_cash_account_has_empty_inventory(decimal cash)
    {
        using var fixture = new Fixture();
        var account = Account(cash: cash);
        fixture.Wire.Balances = Balances(cash, 0, []);

        var evidence = await fixture.Source.CheckAsync(account, default);

        Assert.Equal(new LiveActivationEvidence(LiveRiskTests.Account, "synthetic-account", Fingerprint,
            account.Mandate.Version, account.Snapshot.Version, LiveRiskTests.Now, true, true, true, true), evidence);
        Assert.Equal(1, fixture.Wire.PermissionProbes);
    }

    [Theory]
    [InlineData(LiveOrderState.Acknowledged)]
    [InlineData(LiveOrderState.Open)]
    [InlineData(LiveOrderState.PartiallyFilled)]
    public async Task Owned_budget_buy_allows_broker_reserve_below_local_fee_reserve(LiveOrderState state)
    {
        using var fixture = new Fixture();
        var partial = state == LiveOrderState.PartiallyFilled;
        var order = Buy(state, reservedCash: partial ? 60m : 100m) with { FilledQuantity = partial ? .0004m : 0m };
        LivePosition[] positions = partial ? [new("BTC-THB", .0004m, 0, 100000m)] : [];
        var account = Account([order], positions);
        fixture.Wire.Opens = [Open(order, partial ? 59m : 99m)];
        fixture.Wire.Balances = Balances(account.Snapshot.CashBalance, partial ? 59m : 99m, positions);

        var evidence = await fixture.Source.CheckAsync(account, default);

        Assert.True(evidence.PermissionsVerified);
        Assert.False(evidence.InitialInventoryEmpty);
        Assert.Equal(partial ? 60m : 100m, account.Snapshot.CashReserved);
        Assert.Equal(partial ? 60m : 100m, Assert.Single(account.Orders).ReservedCash);
    }

    [Theory]
    [InlineData("wallet-exceeds-local")]
    [InlineData("wallet-below-open-sum")]
    [InlineData("wallet-above-open-sum")]
    public async Task Buy_reserves_require_local_bounds_and_exact_broker_open_sum(string mismatch)
    {
        using var fixture = new Fixture();
        var order = Buy();
        var account = Account([order]);
        var walletReserved = mismatch switch
        {
            "wallet-exceeds-local" => 101m,
            "wallet-below-open-sum" => 98m,
            "wallet-above-open-sum" => 100m,
            _ => 100m
        };
        fixture.Wire.Opens = [Open(order, 99m)];
        fixture.Wire.Balances = Balances(1000m, walletReserved, []);

        await Unavailable(fixture, account);
    }

    [Theory]
    [InlineData("99", true)]
    [InlineData("101", false)]
    public async Task Each_buy_is_bounded_by_its_own_reserve_even_when_aggregate_wallet_reserve_is_bounded(
        string firstAmount, bool expected)
    {
        using var fixture = new Fixture();
        var first = Buy();
        var second = Buy(reservedCash: 40m) with
        {
            BrokerId = "second-owned-order",
            Intent = first.Intent with
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Quantity = .0004m,
                BuyBudget = 40m
            }
        };
        var account = Account([first, second]);
        var rawAmount = decimal.Parse(firstAmount, CultureInfo.InvariantCulture);
        fixture.Wire.Opens = [Open(first, rawAmount), Open(second, 39m)];
        fixture.Wire.Balances = Balances(1000m, rawAmount + 39m, []);

        if (expected) Assert.True((await fixture.Source.CheckAsync(account, default)).PermissionsVerified);
        else await Unavailable(fixture, account);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("foreign-order")]
    [InlineData("foreign-client")]
    [InlineData("foreign-symbol")]
    [InlineData("wrong-side")]
    [InlineData("wrong-price")]
    [InlineData("market-type")]
    [InlineData("zero-amount")]
    [InlineData("zero-price")]
    public async Task Open_order_evidence_must_be_unique_owned_positive_limit_order(string mismatch)
    {
        using var fixture = new Fixture();
        var order = Buy();
        var open = Open(order, 100m);
        var changed = mismatch switch
        {
            "foreign-order" => open with { OrderId = "foreign-order" },
            "foreign-client" => open with { ClientId = "foreign-client" },
            "foreign-symbol" => open with { Symbol = "ETH-THB" },
            "wrong-side" => open with { Side = OrderSide.Sell },
            "wrong-price" => open with { Rate = 100001m },
            "market-type" => open with { Type = "market" },
            "zero-amount" => open with { RawAmount = 0 },
            "zero-price" => open with { Rate = 0 },
            _ => open
        };
        fixture.Wire.Opens = mismatch == "duplicate" ? [open, open] : [changed];
        fixture.Wire.Balances = Balances(1000m, 100m, []);

        await Unavailable(fixture, Account([order]));
    }

    [Theory]
    [InlineData(LiveOrderState.Proposed)]
    [InlineData(LiveOrderState.Rejected)]
    [InlineData(LiveOrderState.Reserved)]
    [InlineData(LiveOrderState.Dispatching)]
    [InlineData(LiveOrderState.Unknown)]
    [InlineData(LiveOrderState.Filled)]
    [InlineData(LiveOrderState.Cancelled)]
    public async Task Broker_open_cannot_match_local_order_outside_confirmed_open_states(LiveOrderState state)
    {
        using var fixture = new Fixture();
        var order = Buy(state);
        fixture.Wire.Opens = [Open(order, 100m)];
        fixture.Wire.Balances = Balances(1000m, 100m, []);

        await Unavailable(fixture, Account([order]));
    }

    [Theory]
    [InlineData(LiveOrderState.Acknowledged)]
    [InlineData(LiveOrderState.Open)]
    [InlineData(LiveOrderState.PartiallyFilled)]
    public async Task Every_confirmed_local_open_order_must_still_exist_at_broker(LiveOrderState state)
    {
        using var fixture = new Fixture();

        await Unavailable(fixture, Account([Buy(state)]));
    }

    [Fact]
    public async Task Current_trade_permission_is_probed_each_time_and_revocation_overrides_saved_grant()
    {
        using var fixture = new Fixture();
        var account = Account();
        Assert.True((await fixture.Source.CheckAsync(account, default)).PermissionsVerified);
        fixture.Wire.PermissionResponse = "{\"error\":52}";

        var revoked = await fixture.Source.CheckAsync(account, default);

        Assert.False(revoked.PermissionsVerified);
        Assert.True(revoked.CredentialsVerified);
        Assert.Equal(2, fixture.Wire.PermissionProbes);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("network")]
    [InlineData("server")]
    public async Task Unavailable_permission_evidence_cannot_reuse_saved_grant(string failure)
    {
        using var fixture = new Fixture();
        fixture.Wire.PermissionFailure = failure;

        await Unavailable(fixture, Account());

        Assert.Equal(1, fixture.Wire.PermissionProbes);
    }

    [Theory]
    [InlineData("cash-total")]
    [InlineData("coin-total")]
    [InlineData("coin-reserved")]
    [InlineData("foreign-coin")]
    [InlineData("missing-coin")]
    public async Task Conservative_cash_reserves_do_not_relax_exact_wallet_and_position_matching(string mismatch)
    {
        using var fixture = new Fixture();
        LivePosition[] positions = [new("BTC-THB", .002m, .001m, 100000m)];
        var sell = Sell();
        var account = Account([sell], positions);
        fixture.Wire.Opens = [Open(sell, .001m)];
        var balances = Balances(mismatch == "cash-total" ? 999m : 1000m, 0, positions).ToList();
        if (mismatch == "coin-total") balances[1] = new("BTC", .002m, .001m, .003m);
        if (mismatch == "coin-reserved") balances[1] = new("BTC", .002m, 0, .002m);
        if (mismatch == "foreign-coin") balances.Add(new("ETH", 1m, 0, 1m));
        if (mismatch == "missing-coin") balances.RemoveAt(1);
        fixture.Wire.Balances = balances;

        await Unavailable(fixture, account);
    }

    [Theory]
    [InlineData("0.001", true)]
    [InlineData("0.0009", false)]
    [InlineData("0.0011", false)]
    public async Task Sell_open_amount_must_equal_exact_local_reserved_coins(string amount, bool expected)
    {
        using var fixture = new Fixture();
        var sell = Sell();
        LivePosition[] positions = [new("BTC-THB", .002m, .001m, 100000m)];
        var account = Account([sell], positions);
        fixture.Wire.Balances = Balances(1000m, 0, positions);
        fixture.Wire.Opens = [Open(sell, decimal.Parse(amount, CultureInfo.InvariantCulture))];

        if (expected)
        {
            var evidence = await fixture.Source.CheckAsync(account, default);
            Assert.True(evidence.PermissionsVerified);
            Assert.False(evidence.InitialInventoryEmpty);
        }
        else await Unavailable(fixture, account);
    }

    private static LiveOrderRecord Buy(LiveOrderState state = LiveOrderState.Open, decimal reservedCash = 100m)
        => new(LiveRiskTests.Intent with { Quantity = .001m, BuyBudget = 100m }, state, "owned-order",
            reservedCash, 0, 0, "synthetic-request-hash", LiveRiskTests.Now, []);

    private static LiveOrderRecord Sell()
    {
        var buy = Buy();
        return buy with
        {
            Intent = buy.Intent with { Side = OrderSide.Sell, BuyBudget = null },
            ReservedCash = 0,
            ReservedQuantity = .001m
        };
    }

    private static LiveAccountState Account(IReadOnlyList<LiveOrderRecord>? orders = null,
        IReadOnlyList<LivePosition>? positions = null, decimal cash = 1000m)
    {
        orders ??= [];
        positions ??= [];
        return new(LiveRiskTests.Account, LiveRiskTests.Mandate with { CredentialFingerprint = Fingerprint },
            LiveRiskTests.Gate with { CredentialFingerprint = Fingerprint }, LiveRiskTests.Snapshot with
            {
                CashBalance = cash,
                CashReserved = orders.Sum(o => o.ReservedCash),
                Positions = positions,
                Equity = cash + positions.Sum(p => p.Quantity * p.MarketPrice)
            }, orders, [], []);
    }

    private static BrokerOpenOrderEvidence Open(LiveOrderRecord order, decimal amount)
        => new(order.Intent.Symbol, order.BrokerId!, order.Intent.Side, "limit", order.Intent.LimitPrice,
            amount, 0, 0, 0, "0", "0", order.Intent.Id.ToString("N"), LiveRiskTests.Now.ToUnixTimeMilliseconds());

    private static IReadOnlyList<BrokerWalletBalance> Balances(decimal cash, decimal reserved,
        IReadOnlyList<LivePosition> positions)
        => [new("THB", cash - reserved, reserved, cash), .. positions.Select(p => new BrokerWalletBalance(
            p.Symbol[..^4], p.Quantity - p.ReservedQuantity, p.ReservedQuantity, p.Quantity))];

    private static async Task Unavailable(Fixture fixture, LiveAccountState account)
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.CheckAsync(account, default));
        Assert.DoesNotContain("test-key", error.ToString());
        Assert.DoesNotContain("test-secret", error.ToString());
    }

    [Fact]
    public async Task Receipt_mode_preflight_requires_connected_observer_before_private_broker_reads()
    {
        using var fixture = new Fixture(receiptMode: true);
        await Unavailable(fixture, Account());
        Assert.Equal(0, fixture.Wire.PermissionProbes);
    }

    [Fact]
    public async Task Receipt_mode_preparation_requires_connected_observer_before_storage_or_broker_reads()
    {
        using var fixture = new Fixture(receiptMode: true);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Broker.PrepareAsync(LiveRiskTests.Intent, default));
        Assert.Equal("BITKUB_ORDER_RECEIPT_STREAM_REQUIRED", error.Message);
        Assert.Equal(0, fixture.Wire.PermissionProbes);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("quantara-preflight-test-").FullName;
        private readonly ServiceProvider provider;
        public Wire Wire { get; } = new();
        public ILiveActivationSource Source { get; }
        public ILiveBroker Broker => provider.GetRequiredService<ILiveBroker>();

        public Fixture(bool receiptMode = false)
        {
            var credentials = Path.Combine(directory, "credentials.env");
            var verification = Path.Combine(directory, "verification.json");
            File.WriteAllText(credentials, "BITKUB_API_KEY=test-key\nBITKUB_API_SECRET=test-secret\n");
            var rules = new BitkubVerifiedExecutionRules("synthetic-confirmation", new string('0', 64), .003m,
                receiptMode ? BitkubBuyHistoryAmount.AuthenticatedOrderReceipts : BitkubBuyHistoryAmount.QuoteBeforeCashFee, [
                    new("BTC-THB", 1m, .00000001m, 1m, 10m, 8, .00000001m),
                    new("ETH-THB", 1m, .00000001m, 1m, 10m, 8, .00000001m),
                    new("SOL-THB", 1m, .00000001m, 1m, 10m, 8, .00000001m)]);
            File.WriteAllText(verification, JsonSerializer.Serialize(new BitkubLiveVerification("synthetic-account",
                Fingerprint, "synthetic-confirmation", new string('0', 64), true, true, rules)));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LiveExecution:Enabled"] = "true",
                ["LiveExecution:AccountId"] = LiveRiskTests.Account.Value.ToString(),
                ["LiveExecution:VerificationFile"] = verification,
                ["Bitkub:CredentialsFile"] = credentials,
                ["ConnectionStrings:LiveTrading"] = "Host=synthetic;Database=synthetic"
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new Clock());
            BitkubLiveRuntime.Configure(services, config);
            services.Replace(ServiceDescriptor.Singleton<BitkubLiveBroker>(_ =>
                new("test-key", "test-secret", Wire, new Clock())));
            provider = services.BuildServiceProvider();
            Source = provider.GetRequiredService<ILiveActivationSource>();
        }

        public void Dispose()
        {
            provider.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => LiveRiskTests.Now; }

    private sealed class Wire : HttpMessageHandler
    {
        public IReadOnlyList<BrokerWalletBalance> Balances { get; set; } = [new("THB", 1000m, 0, 1000m)];
        public IReadOnlyList<BrokerOpenOrderEvidence> Opens { get; set; } = [];
        public string PermissionResponse { get; set; } = "{\"error\":0,\"result\":\"synthetic-discarded-token-1234567890\"}";
        public string? PermissionFailure { get; set; }
        public int PermissionProbes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://api.bitkub.com", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v3/servertime")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.False(request.Headers.Contains("X-BTK-APIKEY"));
                return Response(LiveRiskTests.Now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            }
            Assert.Equal("test-key", request.Headers.GetValues("X-BTK-APIKEY").Single());
            Assert.Single(request.Headers.GetValues("X-BTK-SIGN"));
            if (path == "/api/v3/market/wstoken")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("{}", await request.Content!.ReadAsStringAsync(token));
                PermissionProbes++;
                if (PermissionFailure == "network") throw new HttpRequestException("test-secret");
                return Response(PermissionFailure == "malformed" ? "{\"error\":90}" : PermissionResponse,
                    PermissionFailure == "server" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            if (path == "/api/v4/wallet/balances")
                return Response(JsonSerializer.Serialize(new { code = "0", data = Balances.Select(b => new
                { currency = b.Currency, available = Number(b.Available), reserved = Number(b.Reserved), total = Number(b.Total) }) }));
            Assert.Equal("/api/v3/market/my-open-orders", path);
            var symbol = request.RequestUri.Query switch
            {
                "?sym=btc_thb" => "BTC-THB",
                "?sym=eth_thb" => "ETH-THB",
                "?sym=sol_thb" => "SOL-THB",
                _ => throw new InvalidOperationException("Unexpected synthetic query.")
            };
            return Response(JsonSerializer.Serialize(new { error = 0, result = Opens.Where(o => o.Symbol == symbol).Select(o => new
            {
                id = o.OrderId, side = o.Side == OrderSide.Buy ? "buy" : "sell", type = o.Type,
                rate = Number(o.Rate), amount = Number(o.RawAmount), receive = Number(o.RawReceive),
                fee = Number(o.ReportedFee), credit = Number(o.FeeCredit), parent_id = o.ParentOrderId,
                super_id = o.SuperOrderId, client_id = o.ClientId, ts = o.Timestamp
            }) }));
        }

        private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(body) };
    }
}
