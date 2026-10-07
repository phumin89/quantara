using System.Net;
using System.Text;
using System.Text.Json;
using TradingCompany.AgentRuntime;
using TradingCompany.Api;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CoinResearchProviderTests
{
    [Fact]
    public async Task Full_public_pipeline_polls_without_repeating_document_quota_and_updates_shortlist_after_depth_change()
    {
        using var fixture = new Fixture();
        var first = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(3, first.Findings.Count); Assert.Equal(6, first.Sources.Count);
        Assert.Equal(3, fixture.Model.Calls);
        Assert.Contains(first.Watchlist, c => c.Symbol == "DOT-THB" && !c.ExecutionAllowed);
        var requests = fixture.HttpCalls;
        Assert.Equal(first, await fixture.Provider.ReadAsync(fixture.Packet, default));
        Assert.Equal(requests, fixture.HttpCalls);
        fixture.Clock.Now += TimeSpan.FromMinutes(16);
        var stable = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(first.ChangeKey, stable.ChangeKey); Assert.Equal(3, fixture.Model.Calls);
        fixture.ThinDot = true; fixture.Clock.Now += TimeSpan.FromMinutes(16);
        var changed = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.DoesNotContain(changed.Watchlist, c => c.Symbol == "DOT-THB");
        Assert.NotEqual(first.ChangeKey, changed.ChangeKey);
        Assert.Equal(3, fixture.Model.Calls);
        Assert.Equal(["BTC-THB"], fixture.Packet.Limits.AllowedSymbols);
    }

    [Fact]
    public async Task Timeout_in_public_market_feed_is_saved_as_a_gap_and_does_not_kill_research_host()
    {
        using var fixture = new Fixture { MarketTimeout = true };
        var brief = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Empty(brief.Watchlist);
        Assert.Contains(brief.Gaps, g => g.Contains("Bitkub"));
        Assert.Equal(3, brief.Findings.Count);
        Assert.True(CoinResearchValidation.IsValid(brief, fixture.Clock.Now));
    }

    [Fact]
    public async Task Unchanged_document_refresh_reuses_model_artifacts_but_changed_content_requires_new_analysis()
    {
        using var fixture = new Fixture();
        var original = await fixture.Provider.ReadAsync(fixture.Packet, default);
        fixture.Clock.Now += TimeSpan.FromHours(25);
        var refreshed = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(original.ChangeKey, refreshed.ChangeKey); Assert.Equal(3, fixture.Model.Calls);
        fixture.Revision = 1; fixture.Clock.Now += TimeSpan.FromHours(25);
        var revised = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.NotEqual(original.ChangeKey, revised.ChangeKey); Assert.Equal(6, fixture.Model.Calls);
    }

    [Fact]
    public async Task ObserveMarketsAsync_reads_public_bitkub_shape_and_filters_non_spot_markets()
    {
        var handler = new Handler((request, _) =>
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("symbols", StringComparison.Ordinal)
                ? "{\"error\":0,\"result\":[{\"symbol\":\"BTC_THB\",\"quote_asset\":\"THB\",\"source\":\"exchange\",\"market_segment\":\"SPOT\",\"status\":\"active\",\"freeze_buy\":false,\"freeze_sell\":false,\"freeze_cancel\":false},{\"symbol\":\"G_THB\",\"quote_asset\":\"THB\",\"source\":\"exchange\",\"market_segment\":\"SPOT\",\"status\":\"active\",\"freeze_buy\":false,\"freeze_sell\":false,\"freeze_cancel\":false},{\"symbol\":\"BAD_USD\",\"quote_asset\":\"USD\",\"source\":\"exchange\",\"market_segment\":\"SPOT\",\"status\":\"active\",\"freeze_buy\":false,\"freeze_sell\":false,\"freeze_cancel\":false}]}"
                : "[{\"symbol\":\"BTC_THB\",\"highest_bid\":\"100\",\"lowest_ask\":\"101\",\"quote_volume\":\"200000\",\"percent_change\":\"2\"},{\"symbol\":\"G_THB\",\"highest_bid\":\"10\",\"lowest_ask\":\"11\",\"quote_volume\":\"300000\",\"percent_change\":\"-1\"}]";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.RequestMessage = request;
            return Task.FromResult(response);
        });
        var provider = new CoinResearchProvider(new Factory(handler), null!, null!, TimeProvider.System);
        var rows = await provider.ObserveMarketsAsync(default);
        Assert.Equal(2, rows.Count);
        var btc = Assert.Single(rows, row => row.Symbol == "BTC-THB");
        Assert.Equal(200000m, btc.QuoteVolume24h);
        Assert.True(btc.Active);
        Assert.False(btc.Frozen);
        var g = Assert.Single(rows, row => row.Symbol == "G-THB");
        Assert.Equal(300000m, g.QuoteVolume24h);
        Assert.True(g.Active);
        Assert.False(g.Frozen);
        Assert.True(CoinResearchValidation.ValidSymbol("G-THB"));
        Assert.False(CoinResearchValidation.ValidSymbol("BAD_USD"));
        Assert.False(CoinResearchValidation.ValidSymbol("ABCDEFGHIJKLMNOP-THB"));
        Assert.False(CoinResearchValidation.ValidSymbol("-THB"));
        Assert.False(CoinResearchValidation.ValidSymbol("g-THB"));
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 7, 3, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Model : ITradingAgentGateway
    {
        public int Calls;
        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken token)
        {
            Calls++;
            using var json = JsonDocument.Parse(request.InputJson);
            var doc = json.RootElement.GetProperty("documents")[0];
            var output = JsonSerializer.Serialize(new { symbol = json.RootElement.GetProperty("symbol").GetString(), summary = "ข้อมูลจากโครงการ ยังขาดแหล่งยืนยันอิสระ",
                claims = new[] { new { statement = "สมมติฐานความปลอดภัยจากต้นฉบับ", assessment = "PROJECT_CLAIM",
                    sourceId = doc.GetProperty("source").GetProperty("id").GetString(), quote = doc.GetProperty("text").GetString()![..40] } } });
            return Task.FromResult(new AgentModelResponse(output, "synthetic-research", "fixture", 5, 5, TimeSpan.Zero));
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("quantara-public-research-test-").FullName;
        private readonly JsonAgentDecisionArtifactStore artifacts;
        public readonly Clock Clock = new();
        public readonly Model Model = new();
        public readonly CoinResearchProvider Provider;
        public bool ThinDot, MarketTimeout;
        public int Revision, HttpCalls;
        public LiveResearchPacket Packet => new(new(TradingCompany.Domain.TradingMode.Live, LiveRiskTests.Account.Value.ToString("N")), 1,
            LiveRiskTests.Snapshot with { ObservedAt = Clock.Now }, new(1000, .2m, 3, .003m, .01m, ["BTC-THB"]), true, false);
        public Fixture()
        {
            artifacts = new(Path.Combine(directory, "artifacts"));
            var handler = new Handler((request, _) =>
            {
                HttpCalls++; Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Headers.Authorization);
                var uri = request.RequestUri!; string body;
                if (uri.Host == "api.bitkub.com")
                {
                    if (MarketTimeout) throw new TaskCanceledException("Synthetic HTTP timeout, caller not cancelled");
                    body = uri.AbsolutePath.EndsWith("symbols") ? JsonSerializer.Serialize(new { error = 0, result = new[] { "BTC", "ETH", "DOT" }.Select(s => new
                        { symbol = s + "_THB", quote_asset = "THB", source = "exchange", market_segment = "SPOT", status = "active", freeze_buy = false, freeze_sell = false, freeze_cancel = false }) }) :
                        uri.AbsolutePath.EndsWith("ticker") ? JsonSerializer.Serialize(new[] { "BTC", "ETH", "DOT" }.Select(s => new
                        { symbol = s + "_THB", highest_bid = "100", lowest_ask = "100.1", quote_volume = "500000", percent_change = "-2" })) :
                        JsonSerializer.Serialize(new { error = 0, result = new { asks = new[] { new[] { 100.1m, ThinDot && uri.Query.Contains("dot_thb") ? 1m : 200m } },
                            bids = new[] { new[] { 100m, ThinDot && uri.Query.Contains("dot_thb") ? 1m : 200m } } } });
                }
                else
                {
                    var text = "Synthetic public document with security assumptions and original protocol design. " + uri.AbsolutePath + " version " + Revision + new string('x', 140);
                    body = uri.AbsolutePath.EndsWith(".pdf") ? "%PDF-" + text : uri.Host == "raw.githubusercontent.com" ? text : "<main>" + text + "</main>";
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body) });
            });
            var factory = new Factory(handler);
            var reader = new CoinSourceReader(factory.CreateClient("public-research"), Path.Combine(directory, "sources"), Clock,
                (bytes, _) => Task.FromResult(Encoding.UTF8.GetString(bytes)[5..]));
            Provider = new(factory, reader, new CoinCredibilityResearcher(Model, artifacts, "fixture", Clock), Clock);
        }
        public void Dispose() { artifacts.Dispose(); Directory.Delete(directory, true); }
    }
}
