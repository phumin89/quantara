using Quantara.MarketData.Domain.News;
using Quantara.MarketData.Infrastructure.News;

namespace Quantara.MarketData.Checks;

public static class NewsChecks
{
    public static void Run()
    {
        var source = new NewsSource("fed", "Federal Reserve", new Uri("https://www.federalreserve.gov/feed.xml"));
        var xml =
            "<rss><channel><item><title>Rate decision</title><link>https://www.federalreserve.gov/press/a.xml</link><pubDate>Sun, 18 Oct 2026 10:00:00 GMT</pubDate></item><item><title>Bad link</title><link>http://127.0.0.1/secret</link></item></channel></rss>";
        var events = NewsRssParser.Parse(source, xml, DateTimeOffset.UtcNow);
        Assert(events.Count == 1, "unsafe news link was accepted");
        Assert(
            events[0].PublishedAt == new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero),
            "RFC822 publication time was lost"
        );
        var edited = NewsRssParser.Parse(
            source,
            xml.Replace("Rate decision", "Edited rate decision"),
            DateTimeOffset.UtcNow
        );
        Assert(events[0].EventId == edited[0].EventId, "article edit changed event identity");
        Assert(events[0].ContentHash != edited[0].ContentHash, "article edit was not a new revision");
        Assert(!NewsNetworkPolicy.IsPublic(System.Net.IPAddress.Parse("127.0.0.1")), "loopback accepted");
        Assert(!NewsNetworkPolicy.IsPublic(System.Net.IPAddress.Parse("10.0.0.1")), "private address accepted");
        foreach (
            var address in new[]
            {
                "169.254.169.254",
                "100.64.0.1",
                "192.0.2.1",
                "198.18.0.1",
                "::1",
                "fc00::1",
                "::ffff:127.0.0.1",
                "2001:db8::1",
            }
        )
        {
            Assert(
                !NewsNetworkPolicy.IsPublic(System.Net.IPAddress.Parse(address)),
                "reserved news address accepted: " + address
            );
        }

        Assert(NewsNetworkPolicy.IsPublic(System.Net.IPAddress.Parse("8.8.8.8")), "public address rejected");
        var atom =
            "<feed><entry><id>fed-1</id><title>Atom release</title><link href=\"https://www.federalreserve.gov/press/atom.xml\"/><updated>2026-10-18T10:00:00Z</updated></entry></feed>";
        Assert(NewsRssParser.Parse(source, atom, DateTimeOffset.UtcNow).Count == 1, "Atom href was not parsed");
        try
        {
            NewsRssParser.Parse(
                source,
                "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><rss>&e;</rss>",
                DateTimeOffset.UtcNow
            );
            throw new InvalidOperationException("DTD payload was accepted");
        }
        catch (System.Xml.XmlException) { }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
