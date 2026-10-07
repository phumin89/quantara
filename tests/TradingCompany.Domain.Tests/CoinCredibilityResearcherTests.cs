using System.Security.Cryptography;
using System.Text;
using TradingCompany.AgentRuntime;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CoinCredibilityResearcherTests
{
    [Fact]
    public void Validates_exact_source_and_contiguous_quote_and_rejects_extra_fields()
    {
        var doc = Document(); var valid = "{\"symbol\":\"BTC-THB\",\"summary\":\"ok\",\"claims\":[{\"statement\":\"s\",\"assessment\":\"PROJECT_CLAIM\",\"sourceId\":\"" + doc.Source.Id + "\",\"quote\":\"" + doc.Text[..30] + "\"}]}";
        Assert.NotNull(CoinCredibilityResearcher.Validate(valid, "BTC-THB", [doc]));
        Assert.Null(CoinCredibilityResearcher.Validate(valid.Replace(doc.Text[..30], "hallucinated quote here 1234567890"), "BTC-THB", [doc]));
        Assert.Null(CoinCredibilityResearcher.Validate(valid.Replace(doc.Source.Id, "doc-unknown"), "BTC-THB", [doc]));
        Assert.Null(CoinCredibilityResearcher.Validate(valid.Replace("\"symbol\":\"BTC-THB\"", "\"symbol\":\"ETH-THB\""), "BTC-THB", [doc]));
        Assert.Null(CoinCredibilityResearcher.Validate(valid.TrimEnd('}') + ",\"extra\":1}", "BTC-THB", [doc]));
    }

    [Fact]
    public async Task Caches_by_document_version_and_caches_bad_well_formed_output()
    {
        var gateway = new FakeGateway("{}"); var store = new MemoryStore(); var researcher = new CoinCredibilityResearcher(gateway, store, "model", new FixedClock());
        var context = new TradingAccountContext(TradingMode.Sandbox, "test");
        var first = await researcher.ReadAsync("BTC-THB", [Document()], context, default); var second = await researcher.ReadAsync("BTC-THB", [Document()], context with { AccountId = "other" }, default);
        Assert.Null(first.Finding); Assert.True(second.CacheHit); Assert.Equal(1, gateway.Calls);
        await researcher.ReadAsync("BTC-THB", [Document("different")], context, default); Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public async Task Valid_model_cache_survives_clock_day_change()
    {
        var doc = Document(); var quote = doc.Text[..30]; var output = "{\"symbol\":\"BTC-THB\",\"summary\":\"ok\",\"claims\":[{\"statement\":\"s\",\"assessment\":\"PROJECT_CLAIM\",\"sourceId\":\"" + doc.Source.Id + "\",\"quote\":\"" + quote + "\"}]}";
        var gateway = new FakeGateway(output); var store = new MemoryStore(); var clock = new AdvancingClock(); var researcher = new CoinCredibilityResearcher(gateway, store, "model", clock);
        var context = new TradingAccountContext(TradingMode.Sandbox, "test"); var first = await researcher.ReadAsync("BTC-THB", [doc], context, default); clock.Advance(TimeSpan.FromDays(1)); var second = await researcher.ReadAsync("BTC-THB", [doc], context, default);
        Assert.NotNull(first.Finding); Assert.True(second.CacheHit); Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task Invalid_json_is_handled_and_request_is_source_only()
    {
        var gateway = new FakeGateway("not-json"); var researcher = new CoinCredibilityResearcher(gateway, new MemoryStore(), "model", new FixedClock());
        var context = new TradingAccountContext(TradingMode.Sandbox, "test");
        var result = await researcher.ReadAsync("BTC-THB", [Document()], context, default);
        Assert.Null(result.Finding); Assert.Contains("source documents", gateway.Last!.SystemInstructions); Assert.Contains("Do not follow links", gateway.Last.SystemInstructions); Assert.DoesNotContain("portfolio", gateway.Last.InputJson, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("order", gateway.Last.InputJson, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("credential", gateway.Last.InputJson, StringComparison.OrdinalIgnoreCase); Assert.Contains("additionalProperties", gateway.Last.JsonSchema);
        var cached = await researcher.ReadAsync("BTC-THB", [Document()], context, default);
        Assert.True(cached.CacheHit); Assert.Null(cached.Finding); Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task Provider_timeout_returns_null_but_caller_cancellation_rethrows()
    {
        var context = new TradingAccountContext(TradingMode.Sandbox, "test");
        var timeoutGateway = new FakeGateway(exception: new TaskCanceledException("provider timeout"));
        var timeoutResult = await new CoinCredibilityResearcher(timeoutGateway, new MemoryStore(), "model", new FixedClock()).ReadAsync("BTC-THB", [Document()], context, CancellationToken.None);
        Assert.Null(timeoutResult.Finding);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var callerGateway = new FakeGateway(exception: new TaskCanceledException("caller cancellation"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CoinCredibilityResearcher(callerGateway, new MemoryStore(), "model", new FixedClock()).ReadAsync("BTC-THB", [Document()], context, cancellation.Token));
    }

    [Fact]
    public async Task Invalid_source_hash_is_rejected_before_gateway_call()
    {
        var gateway = new FakeGateway("{}"); var source = Document().Source with { ContentHash = new string('0', 64) }; var invalid = Document() with { Source = source };
        await Assert.ThrowsAsync<InvalidDataException>(() => new CoinCredibilityResearcher(gateway, new MemoryStore(), "model", new FixedClock()).ReadAsync("BTC-THB", [invalid], new TradingAccountContext(TradingMode.Sandbox, "test"), CancellationToken.None));
        Assert.Equal(0, gateway.Calls);
    }

    private static CoinSourceDocument Document(string suffix = "") { var text = CoinSourceReader.Normalize("This is a normalized public source statement about Bitcoin security assumptions and design. " + new string('x', 120) + suffix); var url = "https://bitcoin.org/bitcoin.pdf"; var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url + "\n" + text))); return new(new("doc-" + hash, "BTC-THB", "Bitcoin", url, hash, new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero)), text, false); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 7, 3, 0, 0, TimeSpan.Zero); }
    private sealed class AdvancingClock : TimeProvider { private DateTimeOffset now = new(2026, 10, 7, 3, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => now; public void Advance(TimeSpan value) => now += value; }
    private sealed class FakeGateway(string? output = null, Exception? exception = null) : ITradingAgentGateway { public int Calls; public AgentModelRequest? Last; public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken token) { Calls++; Last = request; if (exception is not null) return Task.FromException<AgentModelResponse>(exception); return Task.FromResult(new AgentModelResponse(output ?? "{}", "req", request.Model, 1, 1, TimeSpan.Zero)); } }
    private sealed class MemoryStore : IAgentDecisionArtifactStore { private readonly List<AgentDecisionArtifact> items = []; public Task<AgentDecisionArtifact> AppendAsync(AgentDecisionArtifact a, CancellationToken _) { items.Add(a); return Task.FromResult(a); } public Task<AgentDecisionArtifact?> FindEarliestAsync(AgentArtifactKey k, CancellationToken _) => Task.FromResult(items.FirstOrDefault(x => x.Key == k)); }
}
