using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubWalletReaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-wallet-fixtures", Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new();
    private string Credentials(string secret = "fake-secret")
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ".env");
        File.WriteAllText(path, "BITKUB_API_KEY=fake-key\nBITKUB_API_SECRET=" + secret);
        return path;
    }
    [Fact]
    public async Task Zero_wallet_is_observed_without_execution_authority_and_cached_for_twenty_seconds()
    {
        var calls = 0;
        using var reader = new BitkubWalletReader(Credentials(), clock, (_, _, _) =>
        { calls++; return Task.FromResult<IReadOnlyList<BrokerWalletBalance>>([new("THB",0,0,0),new("BTC",0,0,0)]); });
        var first = await reader.ReadAsync(default);
        Assert.False(first.TradingEnabled); Assert.False(first.AccountIdentityVerified); Assert.True(first.ReadAccessVerified);
        Assert.Equal(0, Assert.Single(first.Balances).Total);
        Assert.DoesNotContain("fake", first.CredentialReference);
        Assert.Same(first, await reader.ReadAsync(default));
        Assert.Equal(1,calls);
        clock.Now += TimeSpan.FromSeconds(20);
        Assert.NotSame(first,await reader.ReadAsync(default));
        Assert.Equal(2,calls);
    }
    [Fact]
    public async Task Credential_change_invalidates_observation_and_invalid_file_never_returns_cached_money()
    {
        var path = Credentials(); var calls = 0;
        using var reader = new BitkubWalletReader(path,clock,(_,_,_) =>
        { calls++; return Task.FromResult<IReadOnlyList<BrokerWalletBalance>>([new("THB",5,1,6)]); });
        var first = await reader.ReadAsync(default);
        Credentials("rotated-secret");
        var changed = await reader.ReadAsync(default);
        Assert.NotEqual(first.CredentialReference,changed.CredentialReference);
        Assert.Equal(2,calls);
        File.WriteAllText(path,"BITKUB_API_KEY=\nBITKUB_API_SECRET=");
        await Assert.ThrowsAsync<InvalidDataException>(()=>reader.ReadAsync(default));
    }
    [Fact]
    public async Task Contradictory_money_and_duplicate_currencies_are_not_wallet_observations()
    {
        foreach (var balances in new BrokerWalletBalance[][]
        { [new("THB",1,0,2)], [new("THB",0,0,0),new("THB",0,0,0)], [new("THB",-1,0,-1)], [new("BTC",0,0,0)] })
        {
            using var reader = new BitkubWalletReader(Credentials(),clock,(_,_,_) =>Task.FromResult<IReadOnlyList<BrokerWalletBalance>>(balances));
            await Assert.ThrowsAsync<InvalidDataException>(()=>reader.ReadAsync(default));
        }
    }
    [Fact]
    public async Task Concurrent_owner_reads_share_one_request_and_clock_rollback_does_not_reuse_future_evidence()
    {
        var calls = 0;
        using var reader = new BitkubWalletReader(Credentials(),clock,(_,_,_) =>
        { calls++; return Task.FromResult<IReadOnlyList<BrokerWalletBalance>>([new("THB",0,0,0)]); });
        await Task.WhenAll(Enumerable.Range(0,10).Select(_=>reader.ReadAsync(default)));
        Assert.Equal(1,calls);
        clock.Now -= TimeSpan.FromSeconds(1);
        await reader.ReadAsync(default);
        Assert.Equal(2,calls);
    }
    public void Dispose() { if(Directory.Exists(root)) Directory.Delete(root,true); }
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-05T07:00:00Z"); public override DateTimeOffset GetUtcNow()=>Now; }
}
