using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TradingCompany.Api;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveExecutionClientTests
{
    private const string Token = "test-coordinator-token-012345678901234567890";
    private static LiveResearchPacket Packet => new(new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N")), 1,
        LiveRiskTests.Snapshot, new(1000, .2m, 3, .003m, .01m, ["BTC-THB"]), true, false);

    [Fact]
    public async Task Advisory_transport_targets_fixed_executor_and_preserves_intent_identity()
    {
        var intent = LiveRiskTests.Intent with { BuyBudget = 400m };
        var wire = new Wire((request, calls) => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = calls == 1 ? JsonContent.Create(Packet) : JsonContent.Create(new LiveIntentReceipt(intent, LiveOrderState.Acknowledged, [])) });
        using var http = new HttpClient(wire) { BaseAddress = new("https://ignored.invalid") };
        var client = new LiveExecutionClient(http, Token, LiveRiskTests.Account);
        var packet = await client.ReadAsync(LiveRiskTests.Account, default);
        Assert.Equal(1000, packet.Snapshot.CashBalance);
        var receipt = await client.SubmitAsync(intent, default);
        Assert.Equal(intent, receipt.Intent);
        Assert.Equal(new[] { $"https://localhost:7443/internal/live/research/{LiveRiskTests.Account.Value:N}",
            "https://localhost:7443/internal/live/intents" }, wire.Uris);
        Assert.All(wire.Tokens, token => Assert.Equal(Token, token));
        var sent = JsonSerializer.Deserialize<LiveIntent>(wire.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(intent, sent);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(503)]
    public async Task Rejected_or_redirected_executor_reply_never_retries_mutation(int code)
    {
        var wire = new Wire((_, _) => new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("private-provider-details") });
        using var http = new HttpClient(wire);
        var client = new LiveExecutionClient(http, Token, LiveRiskTests.Account);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.SubmitAsync(LiveRiskTests.Intent, default));
        Assert.Single(wire.Uris);
        Assert.DoesNotContain("private-provider-details", error.Message);
        Assert.DoesNotContain(Token, error.Message);
    }

    [Fact]
    public async Task Client_refuses_paper_or_other_account_without_network_access()
    {
        var wire = new Wire((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(wire);
        var client = new LiveExecutionClient(http, Token, LiveRiskTests.Account);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.ReadAsync(new(Guid.NewGuid()), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.SubmitAsync(LiveRiskTests.Intent with { Mode = TradingMode.Sandbox }, default));
        Assert.Empty(wire.Uris);
    }

    [Fact]
    public async Task Mismatched_receipt_identity_is_not_an_acknowledgement()
    {
        var wire = new Wire((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonContent.Create(new LiveIntentReceipt(LiveRiskTests.Intent with { Id = Guid.NewGuid() }, LiveOrderState.Acknowledged, [])) });
        using var http = new HttpClient(wire);
        await Assert.ThrowsAsync<InvalidDataException>(() => new LiveExecutionClient(http, Token, LiveRiskTests.Account).SubmitAsync(LiveRiskTests.Intent, default));
        Assert.Single(wire.Uris);
    }

    private sealed class Wire(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Uris { get; } = [];
        public List<string?> Tokens { get; } = [];
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!.AbsoluteUri);
            Tokens.Add(request.Headers.Authorization?.Parameter);
            if (request.Content is not null) Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request, Uris.Count);
        }
    }
}
