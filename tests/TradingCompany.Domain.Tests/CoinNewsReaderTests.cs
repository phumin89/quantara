using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CoinNewsReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 7, 0, 0, TimeSpan.Zero);
    private static readonly CoinNewsFeed Feed = CoinNewsReader.Feeds[0];

    [Fact]
    public void Parses_dated_excerpt_and_any_native_ticker_without_claiming_full_article_or_triggering_orders()
    {
        var xml = Rss("$G new crypto listing", "https://www.coindesk.com/markets/new-listing", Now.AddHours(-1),
            "&lt;script&gt;doNotExecute()&lt;/script&gt;&lt;b&gt;$G&lt;/b&gt; news &amp; risks. U.S. near support, link here.");
        var item = Assert.Single(CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(xml), ["G-THB", "NEAR-THB", "LINK-THB", "S-THB"], Now));
        Assert.Equal(["G-THB"], item.Symbols); Assert.DoesNotContain("doNotExecute", item.EvidenceText);
        Assert.Contains("news & risks", item.EvidenceText); Assert.Equal("REPORT", item.Kind); Assert.Equal("PUBLISHED", item.DateKind);
        Assert.Equal(Now.AddHours(-1), item.SourceAt); Assert.Equal(Now, item.RetrievedAt);
        Assert.Equal("news-" + item.ContentHash, item.Id); Assert.True(CoinNewsValidation.IsValid(Brief([item]), Now));
        Assert.False(CoinNewsValidation.IsValid(Brief([item with { EvidenceText = "tampered" }]), Now));
    }

    [Fact]
    public void Ignores_future_and_old_articles_preserves_updated_semantics_and_requires_explicit_timezone()
    {
        Assert.Empty(CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(Rss("BTC", Url, Now.AddSeconds(1))), ["BTC-THB"], Now));
        Assert.Empty(CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(Rss("BTC", Url, Now.AddDays(-8))), ["BTC-THB"], Now));
        var atom = $"<feed xmlns='http://www.w3.org/2005/Atom'><entry><title>BTC update</title><link href='{Url}'/><updated>{Now.AddHours(-1):O}</updated><summary>BTC event</summary></entry></feed>";
        Assert.Equal("UPDATED", Assert.Single(CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(atom), ["BTC-THB"], Now)).DateKind);
        var missingZone = Rss("BTC", Url, Now).Replace(Now.ToString("r"), "Fri, 09 Oct 2026 07:00:00");
        Assert.Throws<InvalidDataException>(() => CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(missingZone), ["BTC-THB"], Now));
    }

    [Theory]
    [InlineData("https://evil.example/markets/a")]
    [InlineData("https://www.coindesk.com@evil.example/a")]
    [InlineData("http://www.coindesk.com/a")]
    [InlineData("https://www.coindesk.com:444/a")]
    [InlineData("https://www.coindesk.com/a?credential=anything")]
    [InlineData("https://www.coindesk.com/a?")]
    [InlineData("https://www.coindesk.com/a#")]
    public void Rejects_unverified_article_urls(string url) => Assert.Throws<InvalidDataException>(() =>
        CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(Rss("BTC", url, Now)), ["BTC-THB"], Now));

    [Fact]
    public void Rejects_xml_entities_duplicates_unconfigured_feeds_and_oversize_input()
    {
        var dtd = "<!DOCTYPE rss [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><rss><channel><item><title>&xxe;</title></item></channel></rss>";
        Assert.Throws<System.Xml.XmlException>(() => CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(dtd), ["BTC-THB"], Now));
        var item = Rss("BTC", Url, Now).Split("<channel>")[1].Split("</channel>")[0];
        Assert.Throws<InvalidDataException>(() => CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes($"<rss><channel>{item}{item}</channel></rss>"), ["BTC-THB"], Now));
        Assert.Throws<InvalidDataException>(() => CoinNewsReader.Parse(new("CoinDesk", "https://evil.example/rss"), [], [], Now));
        Assert.Throws<InvalidDataException>(() => CoinNewsReader.Parse(Feed, new byte[2 * 1024 * 1024 + 1], [], Now));
    }

    [Fact]
    public void Numeric_symbols_require_markers_and_digit_prefixed_tickers_are_supported()
    {
        var item = Assert.Single(CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(Rss("1INCH crypto price 42; (42) listed", Url, Now)), ["1INCH-THB", "42-THB"], Now));
        Assert.Equal(["1INCH-THB", "42-THB"], item.Symbols);
        var unmarked = Assert.Single(CoinNewsReader.Parse(Feed, Encoding.UTF8.GetBytes(Rss("BTC crypto price 42", Url, Now)), ["BTC-THB", "42-THB"], Now));
        Assert.Equal(["BTC-THB"], unmarked.Symbols);
    }

    [Fact]
    public async Task Polls_fifteen_minutes_archives_original_bytes_and_reports_partial_source_availability()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var clock = new Clock(); var handler = new Handler();
            var reader = new CoinNewsReader(new HttpClient(handler), folder, clock);
            var first = await reader.ReadAsync(["BTC-THB", "ETH-THB"], ["BTC-THB"], default);
            Assert.Equal(2, first.Items.Count); Assert.Equal(3, handler.Calls);
            Assert.Contains(first.Coverage, c => c.Source == "Federal Reserve" && c.Status == "UNAVAILABLE");
            Assert.Contains(first.Coverage, c => c.Source == "Bitkub" && c.Status == "UNAVAILABLE");
            Assert.True(CoinNewsValidation.IsValid(first, clock.Now));
            Assert.Same(first, await reader.ReadAsync(["ETH-THB", "BTC-THB"], ["BTC-THB"], default)); Assert.Equal(3, handler.Calls);
            var raw = Directory.GetFiles(folder, "raw-*.xml"); Assert.Equal(2, raw.Length);
            foreach (var path in raw) Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), path);
            var historical = File.ReadAllBytes(Path.Combine(folder, "brief-" + first.ChangeKey + ".json"));
            clock.Now += TimeSpan.FromMinutes(16);
            var sameContent = await reader.ReadAsync(["BTC-THB", "ETH-THB"], ["BTC-THB"], default);
            Assert.Equal(first.ChangeKey, sameContent.ChangeKey); Assert.Equal(6, handler.Calls);
            Assert.Equal(historical, File.ReadAllBytes(Path.Combine(folder, "brief-" + first.ChangeKey + ".json")));
            handler.Revision++; clock.Now += TimeSpan.FromMinutes(16);
            var changed = await reader.ReadAsync(["BTC-THB", "ETH-THB"], ["BTC-THB"], default);
            Assert.NotEqual(first.ChangeKey, changed.ChangeKey); Assert.Equal(9, handler.Calls);
            Assert.Equal(2, Directory.GetFiles(folder, "brief-*.json").Length);
        }
        finally { Directory.Delete(folder, true); }
    }

    private const string Url = "https://www.coindesk.com/markets/btc";
    private static string Rss(string title, string url, DateTimeOffset date, string text = "BTC feed excerpt") =>
        $"<rss><channel><item><title>{title}</title><link>{url}</link><pubDate>{date:r}</pubDate><description><![CDATA[{text}]]></description></item></channel></rss>";
    private static CoinNewsBrief Brief(IReadOnlyList<CoinNewsItem> items)
    {
        CoinNewsCoverage[] coverage = [new("CoinDesk", "CURRENT")];
        return new(Now, CoinNewsValidation.ChangeKey(items, coverage), items, coverage);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = CoinNewsReaderTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls, Revision;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Headers.Authorization);
            if (request.RequestUri!.Host == "www.federalreserve.gov") throw new TaskCanceledException("Synthetic source timeout");
            var source = request.RequestUri.Host == "www.coindesk.com" ? "BTC" : "ETH";
            var url = source == "BTC" ? Url : "https://blog.ethereum.org/en/2026/10/08/update";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request,
                Content = new StringContent(Rss(source + " event " + Revision, url, CoinNewsReaderTests.Now.AddHours(-1)), Encoding.UTF8, "application/rss+xml") });
        }
    }
}
