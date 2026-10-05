using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TradingCompany.Infrastructure;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveWalletEndpointTests
{
    [Fact]
    public async Task Private_wallet_requires_pairing_and_never_creates_execution_authority()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new(){BaseAddress=new("https://localhost:7443")});
        client.DefaultRequestHeaders.Add("Origin","https://localhost:3000");
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/live/wallet")).StatusCode);
        Assert.Equal(0,factory.Reads);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/owner/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());
        var code = factory.Services.GetRequiredService<OwnerSession>().IssuePairingCode();
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/owner/pair",new {code})).StatusCode);
        var wallet = await client.GetFromJsonAsync<JsonElement>("/live/wallet");
        Assert.Equal("Bitkub",wallet.GetProperty("source").GetString());
        Assert.False(wallet.GetProperty("tradingEnabled").GetBoolean());
        Assert.False(wallet.GetProperty("accountIdentityVerified").GetBoolean());
        Assert.Equal(0,wallet.GetProperty("balances")[0].GetProperty("total").GetDecimal());
        var status = await client.GetFromJsonAsync<JsonElement>("/live/status");
        Assert.Equal("DISABLED",status.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/live/account")).StatusCode);
        Assert.Equal(1,factory.Reads);
        Assert.DoesNotContain("fake-secret",wallet.ToString());
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin","https://attacker.invalid");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/live/wallet")).StatusCode);
    }
    private sealed class Factory : WebApplicationFactory<LiveProgram>
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"quantara-owner-wallet",Guid.NewGuid().ToString("N"));
        public int Reads;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            Directory.CreateDirectory(root);
            var file=Path.Combine(root,".env");
            File.WriteAllText(file,"BITKUB_API_KEY=fake-key\nBITKUB_API_SECRET=fake-secret");
            builder.ConfigureServices(services=>services.AddSingleton(new BitkubWalletReader(file,TimeProvider.System,(_,_,_)=>
            { Reads++; return Task.FromResult<IReadOnlyList<BrokerWalletBalance>>([new("THB",0,0,0)]); })));
        }
        public override async ValueTask DisposeAsync()
        { await base.DisposeAsync(); if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
