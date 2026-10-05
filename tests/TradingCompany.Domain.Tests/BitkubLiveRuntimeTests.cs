using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubLiveRuntimeTests
{
    [Fact]
    public void Disabled_configuration_registers_nothing_and_does_not_read_files()
    {
        var services = new ServiceCollection();
        BitkubLiveRuntime.Configure(services, Config(false, "missing", "missing"));
        Assert.Empty(services);
    }

    [Fact]
    public void Enabled_incomplete_configuration_is_sanitized()
        => AssertInvalid(new ServiceCollection(), Config(true, "missing", "missing"));

    [Theory]
    [InlineData("{\"BrokerAccountReference\":\"acct\",\"BrokerAccountReference\":\"acct\"}")]
    [InlineData("{\"BrokerAccountReference\":\"acct\",\"Rules\":{\"Pairs\":[{\"Symbol\":\"BTC-THB\",\"Symbol\":\"ETH-THB\"}]}}")]
    [InlineData("{\"BrokerAccountReference\":\"acct\",\"Unknown\":1}")]
    [InlineData("not-json")]
    public void Invalid_verification_json_is_sanitized(string json)
    {
        using var fixture = new Fixture(json);
        AssertInvalid(new ServiceCollection(), Config(true, fixture.VerificationPath, fixture.CredentialsPath));
    }

    [Fact]
    public void Oversized_verification_is_rejected_without_exposing_content()
    {
        using var fixture = new Fixture(new string('x', 64 * 1024 + 1));
        AssertInvalid(new ServiceCollection(), Config(true, fixture.VerificationPath, fixture.CredentialsPath));
    }

    [Theory]
    [InlineData("false", "true", "sha256:0000000000000000000000000000000000000000000000000000000000000000", "0.001")]
    [InlineData("true", "false", "sha256:0000000000000000000000000000000000000000000000000000000000000000", "0.001")]
    [InlineData("true", "true", "sha256:bad", "0.001")]
    [InlineData("true", "true", "sha256:0000000000000000000000000000000000000000000000000000000000000000", "0")]
    public void Unverified_metadata_is_rejected(string accountBinding, string tradePermission, string fingerprint, string fee)
    {
        using var fixture = new Fixture(ValidJson(accountBinding, tradePermission, fingerprint, fee));
        AssertInvalid(new ServiceCollection(), Config(true, fixture.VerificationPath, fixture.CredentialsPath));
    }

    [Fact]
    public void Duplicate_pairs_are_rejected()
    {
        var pairs = "\"Pairs\":[{\"Symbol\":\"BTC-THB\",\"BuyQuoteStep\":1,\"SellBaseStep\":1,\"PriceStep\":1,\"MinimumQuoteSize\":10,\"RawQuantityScale\":8,\"RawQuantityStep\":0.00000001},{\"Symbol\":\"BTC-THB\",\"BuyQuoteStep\":1,\"SellBaseStep\":1,\"PriceStep\":1,\"MinimumQuoteSize\":10,\"RawQuantityScale\":8,\"RawQuantityStep\":0.00000001}]";
        using var fixture = new Fixture(ValidJson(rules: pairs));
        AssertInvalid(new ServiceCollection(), Config(true, fixture.VerificationPath, fixture.CredentialsPath));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000g")]
    public void Malformed_evidence_hash_is_rejected(string evidenceHash)
    {
        using var fixture = new Fixture(ValidJson(evidenceHash: evidenceHash));
        AssertInvalid(new ServiceCollection(), Config(true, fixture.VerificationPath, fixture.CredentialsPath));
    }

    [Fact]
    public void Unsupported_symbol_is_rejected()
    {
        var pairs = "\"Pairs\":[{\"Symbol\":\"DOGE-THB\",\"BuyQuoteStep\":1,\"SellBaseStep\":1,\"PriceStep\":1,\"MinimumQuoteSize\":10,\"RawQuantityScale\":8,\"RawQuantityStep\":0.00000001}]";
        using var fixture = new Fixture(ValidJson(rules: pairs));
        AssertInvalid(new ServiceCollection(), Config(true, fixture.VerificationPath, fixture.CredentialsPath));
    }

    [Fact]
    public void Excessive_cash_fee_rate_is_rejected()
    {
        using var fixture = new Fixture(ValidJson(fee: "0.1001"));
        AssertInvalid(new ServiceCollection(), Config(true, fixture.VerificationPath, fixture.CredentialsPath));
    }

    [Fact]
    public void Valid_configuration_registers_lazily_without_reading_credentials_or_network()
    {
        using var fixture = new Fixture(ValidJson());
        var clock = new FixedClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        BitkubLiveRuntime.Configure(services, Config(true, fixture.VerificationPath, fixture.CredentialsPath));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
        Assert.NotNull(provider.GetRequiredService<BitkubConfiguredAccount>());
        Assert.Contains(services, d => d.ServiceType == typeof(ILiveBroker));
        Assert.Contains(services, d => d.ServiceType == typeof(ILiveTradingStore));
    }

    private static IConfiguration Config(bool enabled, string verification, string credentials)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LiveExecution:Enabled"] = enabled.ToString(), ["LiveExecution:AccountId"] = Guid.NewGuid().ToString(),
            ["LiveExecution:VerificationFile"] = Path.GetFullPath(verification), ["Bitkub:CredentialsFile"] = Path.GetFullPath(credentials),
            ["ConnectionStrings:LiveTrading"] = "Host=synthetic;Database=synthetic"
        }).Build();

    private static void AssertInvalid(IServiceCollection services, IConfiguration config)
    {
        var error = Assert.Throws<InvalidOperationException>(() => BitkubLiveRuntime.Configure(services, config));
        Assert.Equal("Live execution configuration is invalid.", error.Message);
        Assert.Null(error.InnerException);
    }

    private static string ValidJson(string account = "true", string permission = "true", string fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000", string fee = "0.001", string? rules = null, string evidenceHash = "0000000000000000000000000000000000000000000000000000000000000000")
        => $"{{\"BrokerAccountReference\":\"acct\",\"CredentialFingerprint\":\"{fingerprint}\",\"ConfirmationReference\":\"confirm\",\"EvidenceHash\":\"{evidenceHash}\",\"AccountBindingVerified\":{account.ToLowerInvariant()},\"TradePermissionVerified\":{permission.ToLowerInvariant()},\"Rules\":{{\"ConfirmationReference\":\"confirm\",\"EvidenceHash\":\"{evidenceHash}\",\"MaximumCashFeeRate\":{fee},\"BuyHistoryAmount\":0,{rules ?? "\"Pairs\":[{\"Symbol\":\"BTC-THB\",\"BuyQuoteStep\":1,\"SellBaseStep\":1,\"PriceStep\":1,\"MinimumQuoteSize\":10,\"RawQuantityScale\":8,\"RawQuantityStep\":0.00000001}]"}}}}}";

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("quantara-live-test-").FullName;
        public string VerificationPath { get; }
        public string CredentialsPath { get; }
        public Fixture(string verification) { VerificationPath = Path.Combine(directory, "verification.json"); CredentialsPath = Path.Combine(directory, "credentials.env"); File.WriteAllText(VerificationPath, verification); }
        public void Dispose() => Directory.Delete(directory, true);
    }

    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch; }
}
