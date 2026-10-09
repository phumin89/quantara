using System.Net;
using System.Text;
using System.Text.Json;
using TradingCompany.AgentRuntime;
using TradingCompany.Api;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CoinResearchProviderTests
{
    [Fact]
    public async Task New_news_reaches_research_change_without_repeating_document_models_or_expanding_authority()
    {
        using var fixture = new Fixture(withNews: true);
        var first = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.NotNull(first.News); Assert.Equal(3, first.News.Items.Count);
        Assert.True(CoinResearchValidation.IsValid(first, fixture.Clock.Now));
        var calls = fixture.HttpCalls; var models = fixture.Model.Calls;
        Assert.Same(first, await fixture.Provider.ReadAsync(fixture.Packet, default)); Assert.Equal(calls, fixture.HttpCalls);
        fixture.Clock.Now += TimeSpan.FromMinutes(16);
        var stable = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(first.ChangeKey, stable.ChangeKey); Assert.Equal(models, fixture.Model.Calls);
        fixture.NewsRevision++; fixture.Clock.Now += TimeSpan.FromMinutes(16);
        var changed = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.NotEqual(stable.ChangeKey, changed.ChangeKey); Assert.NotEqual(stable.News!.ChangeKey, changed.News!.ChangeKey);
        Assert.Equal(models, fixture.Model.Calls); Assert.Equal(["BTC-THB"], fixture.Packet.Limits.AllowedSymbols);
        Assert.Equal(1000m, fixture.Packet.Limits.CapitalAllocation);
        Assert.Contains(changed.Watchlist, candidate => candidate.Symbol == "DOT-THB" && !candidate.ExecutionAllowed);
    }

    [Fact]
    public async Task Optional_news_archive_failure_preserves_valid_market_and_document_evidence()
    {
        using var fixture = new Fixture(withNews: true, newsStorageFailure: true);
        var brief = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.NotEmpty(brief.Watchlist); Assert.NotEmpty(brief.Findings); Assert.NotEmpty(brief.Sources);
        Assert.NotNull(brief.News); Assert.Empty(brief.News.Items);
        Assert.All(brief.News.Coverage, c => Assert.Equal("UNAVAILABLE", c.Status));
        Assert.True(CoinResearchValidation.IsValid(brief, fixture.Clock.Now));
        Assert.Equal(["BTC-THB"], fixture.Packet.Limits.AllowedSymbols);
    }

    [Fact]
    public async Task Full_public_pipeline_polls_without_repeating_document_quota_and_updates_shortlist_after_depth_change()
    {
        using var fixture = new Fixture();
        var first = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(2, first.Findings.Count); Assert.Equal(4, first.Sources.Count);
        Assert.Equal(2, fixture.Model.Calls);
        Assert.Contains(first.Watchlist, c => c.Symbol == "DOT-THB" && !c.ExecutionAllowed);
        var requests = fixture.HttpCalls;
        Assert.Equal(first, await fixture.Provider.ReadAsync(fixture.Packet, default));
        Assert.Equal(requests, fixture.HttpCalls);
        fixture.Clock.Now += TimeSpan.FromMinutes(16);
        var stable = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(first.ChangeKey, stable.ChangeKey); Assert.Equal(2, fixture.Model.Calls);
        fixture.ThinDot = true; fixture.Clock.Now += TimeSpan.FromMinutes(16);
        var changed = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.DoesNotContain(changed.Watchlist, c => c.Symbol == "DOT-THB");
        Assert.NotEqual(first.ChangeKey, changed.ChangeKey);
        Assert.Equal(2, fixture.Model.Calls);
        Assert.Equal(["BTC-THB"], fixture.Packet.Limits.AllowedSymbols);
    }

    [Fact]
    public async Task Timeout_in_public_market_feed_is_saved_as_a_gap_and_does_not_kill_research_host()
    {
        using var fixture = new Fixture { MarketTimeout = true };
        var brief = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Empty(brief.Watchlist);
        Assert.Contains(brief.Gaps, g => g.Contains("Bitkub"));
        Assert.Single(brief.Findings);
        Assert.Equal(2, brief.Sources.Count);
        Assert.Equal(1, fixture.Model.Calls);
        Assert.True(CoinResearchValidation.IsValid(brief, fixture.Clock.Now));
    }

    [Fact]
    public async Task Unchanged_document_refresh_reuses_model_artifacts_but_changed_content_requires_new_analysis()
    {
        using var fixture = new Fixture();
        var original = await fixture.Provider.ReadAsync(fixture.Packet, default);
        fixture.Clock.Now += TimeSpan.FromHours(25);
        var refreshed = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(original.ChangeKey, refreshed.ChangeKey); Assert.Equal(2, fixture.Model.Calls);
        fixture.Revision = 1; fixture.Clock.Now += TimeSpan.FromHours(25);
        var revised = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.NotEqual(original.ChangeKey, revised.ChangeKey); Assert.Equal(4, fixture.Model.Calls);
    }

    [Fact]
    public async Task Configurable_market_symbols_cover_all_eight_supported_coins_with_source_and_payload_bounds()
    {
        using var fixture = new Fixture { Symbols = ["BTC", "ETH", "SOL", "USDT", "NEAR", "XRP", "DOGE", "ADA"], AllowedSymbols = ["BTC-THB", "ETH-THB", "SOL-THB", "USDT-THB", "NEAR-THB", "XRP-THB", "DOGE-THB", "ADA-THB"] };
        fixture.Model.LongThai = true;
        var brief = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(8, brief.Findings.Count);
        Assert.Equal(11, brief.Sources.Count);
        Assert.True(fixture.Model.Calls <= 8);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(brief).Length <= 24 * 1024);
        Assert.True(CoinResearchValidation.IsValid(brief, fixture.Clock.Now));
        Assert.All(brief.Findings, f => Assert.True(f.Summary.Length <= 140));
        Assert.Equal(8, fixture.Model.Calls);
        fixture.Clock.Now += TimeSpan.FromMinutes(16);
        Assert.Equal(8, (await fixture.Provider.ReadAsync(fixture.Packet, default)).Findings.Count);
        Assert.Equal(8, fixture.Model.Calls);
    }

    [Fact]
    public async Task Unsupported_market_symbol_is_reported_as_an_explicit_gap()
    {
        using var fixture = new Fixture { Symbols = ["BTC", "UNSUPPORTED"], AllowedSymbols = ["BTC-THB", "UNSUPPORTED-THB"] };
        var brief = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Contains(brief.Gaps, gap => gap.Contains("UNSUPPORTED-THB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cached_fifteen_minute_scans_rotate_all_twelve_holdings_and_keep_a_new_opportunity()
    {
        var held = Enumerable.Range(0, 12).Select(i => $"H{i:00}-THB").ToArray();
        using var fixture = new Fixture { Symbols = [.. held.Select(s => s[..^4]), "NEW"],
            AllowedSymbols = [.. held, "NEW-THB"], HeldSymbols = held };
        var first = await fixture.Provider.ReadAsync(fixture.Packet, default);
        var calls = fixture.HttpCalls;
        fixture.Clock.Now += TimeSpan.FromMinutes(14);
        Assert.Same(first, await fixture.Provider.ReadAsync(fixture.Packet, default));
        Assert.Equal(calls, fixture.HttpCalls);
        fixture.Clock.Now += TimeSpan.FromMinutes(1);
        var second = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.NotEqual(first.ChangeKey, second.ChangeKey);
        Assert.All(new[] { first, second }, brief =>
        {
            Assert.Equal(8, brief.Watchlist.Count);
            Assert.Equal("NEW-THB", Assert.Single(brief.Watchlist, c => !c.Held).Symbol);
        });
        Assert.Equal(held, first.Watchlist.Concat(second.Watchlist).Where(c => c.Held)
            .Select(c => c.Symbol).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(13, fixture.Packet.Limits.AllowedSymbols.Count);
        Assert.Equal(12, fixture.Packet.Snapshot.Positions.Count);
        Assert.Equal(0, fixture.Model.Calls);
    }

    [Fact]
    public async Task Shortlisted_crv_is_sourced_and_cached_without_expanding_execution_authority()
    {
        using var fixture = new Fixture { Symbols = ["BTC", "CRV"] };
        var brief = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.False(Assert.Single(brief.Watchlist, c => c.Symbol == "CRV-THB").ExecutionAllowed);
        var source = Assert.Single(brief.Sources, s => s.Symbol == "CRV-THB");
        Assert.Equal("https://raw.githubusercontent.com/curvefi/docs/main/docs/user/curve-tokens/crv.md", source.Url);
        var finding = Assert.Single(brief.Findings, f => f.Symbol == "CRV-THB");
        Assert.All(finding.Claims, claim => { Assert.Equal(source.Id, claim.SourceId); Assert.Equal("PROJECT_CLAIM", claim.Assessment); });
        Assert.DoesNotContain(brief.Gaps, gap => gap.Contains("CRV-THB", StringComparison.Ordinal));
        Assert.True(CoinResearchValidation.IsValid(brief, fixture.Clock.Now));
        Assert.Equal(2, fixture.Model.Calls);

        fixture.Clock.Now += TimeSpan.FromHours(25);
        var refreshed = await fixture.Provider.ReadAsync(fixture.Packet, default);
        Assert.Equal(source.Id, Assert.Single(refreshed.Sources, s => s.Symbol == "CRV-THB").Id);
        Assert.Equal(brief.ChangeKey, refreshed.ChangeKey);
        Assert.Equal(2, fixture.Model.Calls);
        Assert.False(Assert.Single(refreshed.Watchlist, c => c.Symbol == "CRV-THB").ExecutionAllowed);
        Assert.Equal(["BTC-THB"], fixture.Packet.Limits.AllowedSymbols);
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
        public bool LongThai;
        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken token)
        {
            Calls++;
            using var json = JsonDocument.Parse(request.InputJson);
            var doc = json.RootElement.GetProperty("documents")[0];
            var output = JsonSerializer.Serialize(new { symbol = json.RootElement.GetProperty("symbol").GetString(), summary = LongThai ? new string('ก', 600) : "ข้อมูลจากโครงการ ยังขาดแหล่งยืนยันอิสระ",
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
        public IReadOnlyList<string> Symbols { get; set; } = ["BTC", "ETH", "DOT"];
        public IReadOnlyList<string> AllowedSymbols { get; set; } = ["BTC-THB"];
        public IReadOnlyList<string> HeldSymbols { get; set; } = [];
        public int Revision, NewsRevision, HttpCalls;
        public LiveResearchPacket Packet => new(new(TradingCompany.Domain.TradingMode.Live, LiveRiskTests.Account.Value.ToString("N")), 1,
            LiveRiskTests.Snapshot with { ObservedAt = Clock.Now, Positions = HeldSymbols.Select(s => new LivePosition(s, 1, 0, 100)).ToArray(),
                Equity = 1000 + HeldSymbols.Count * 100 }, new(1000, .2m, Math.Max(3, AllowedSymbols.Count), .003m, .01m, AllowedSymbols), true, false);
        public Fixture(bool withNews = false, bool newsStorageFailure = false)
        {
            artifacts = new(Path.Combine(directory, "artifacts"));
            var handler = new Handler((request, _) =>
            {
                HttpCalls++; Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Headers.Authorization);
                var uri = request.RequestUri!; string body;
                if (CoinNewsReader.Feeds.Any(f => f.Url == uri.AbsoluteUri))
                {
                    var article = uri.Host == "www.coindesk.com" ? "https://www.coindesk.com/markets/btc" :
                        uri.Host == "blog.ethereum.org" ? "https://blog.ethereum.org/en/2026/10/07/update" :
                        "https://www.federalreserve.gov/newsevents/pressreleases/monetary20261007a.htm";
                    body = $"<rss><channel><item><title>BTC ETH crypto event {NewsRevision}</title><link>{article}</link>" +
                        "<pubDate>Wed, 07 Oct 2026 02:00:00 GMT</pubDate><description>BTC ETH public feed excerpt</description></item></channel></rss>";
                }
                else if (uri.Host == "api.bitkub.com")
                {
                    if (MarketTimeout) throw new TaskCanceledException("Synthetic HTTP timeout, caller not cancelled");
                    body = uri.AbsolutePath.EndsWith("symbols") ? JsonSerializer.Serialize(new { error = 0, result = Symbols.Select(s => new
                        { symbol = s + "_THB", quote_asset = "THB", source = "exchange", market_segment = "SPOT", status = "active", freeze_buy = false, freeze_sell = false, freeze_cancel = false }) }) :
                        uri.AbsolutePath.EndsWith("ticker") ? JsonSerializer.Serialize(Symbols.Select(s => new
                        { symbol = s + "_THB", highest_bid = "100", lowest_ask = "100.1", quote_volume = "500000", percent_change = "-2" })) :
                        JsonSerializer.Serialize(new { error = 0, result = new { asks = new[] { new[] { 100.1m, ThinDot && uri.Query.Contains("dot_thb") ? 1m : 200m } },
                            bids = new[] { new[] { 100m, ThinDot && uri.Query.Contains("dot_thb") ? 1m : 200m } } } });
                }
                else
                {
                    var text = "Synthetic public document with security assumptions and original protocol design. " + uri.AbsolutePath + " version " + Revision + new string('x', 140);
                    body = uri.AbsolutePath.EndsWith(".pdf") ? "%PDF-" + text : uri.Host == "raw.githubusercontent.com" ? text : "<main>" + text + "</main>";
                }
                var content = uri.AbsolutePath.EndsWith(".pdf") || uri.Host == "raw.githubusercontent.com" ? new StringContent(body) : new StringContent(body, Encoding.UTF8, "text/html");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = content });
            });
            var factory = new Factory(handler);
            var reader = new CoinSourceReader(factory.CreateClient("public-research"), Path.Combine(directory, "sources"), Clock,
                (bytes, _) => Task.FromResult(Encoding.UTF8.GetString(bytes)[5..]));
            if (newsStorageFailure) File.WriteAllText(Path.Combine(directory, "news"), "Synthetic file blocks news directory creation");
            Provider = new(factory, reader, new CoinCredibilityResearcher(Model, artifacts, "fixture", Clock), Clock,
                news: withNews ? new CoinNewsReader(factory.CreateClient("public-research"), Path.Combine(directory, "news"), Clock) : null);
        }
        public void Dispose() { artifacts.Dispose(); Directory.Delete(directory, true); }
    }
}
