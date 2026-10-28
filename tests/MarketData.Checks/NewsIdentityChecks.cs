using System.Xml.Linq;
using Quantara.MarketData.Domain.News;
using Quantara.MarketData.Infrastructure.News;

namespace Quantara.MarketData.Checks;

public static class NewsIdentityChecks
{
    private static readonly NewsSource source = new(
        "identity-fixture",
        "Identity fixture",
        new Uri("https://www.federalreserve.gov/feed.xml")
    );

    private static readonly DateTimeOffset fetchedAt = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        BlankIdentifiers();
        ArticleLinks();
        Compatibility();
        Console.WriteLine(
            "News identity checks passed: blank identifiers, alternate links and retained nonblank identity."
        );
    }

    private static void BlankIdentifiers()
    {
        foreach (var blank in new[] { "", " \t\n " })
        {
            foreach (var name in new[] { "guid", "id", "both" })
            {
                var first = Item("https://www.federalreserve.gov/press/one.htm");
                var second = Item("https://www.federalreserve.gov/press/two.htm");
                foreach (var item in new[] { first, second })
                {
                    if (name is "guid" or "both")
                    {
                        item.Add(new XElement("guid", blank));
                    }

                    if (name is "id" or "both")
                    {
                        item.Add(new XElement("id", blank));
                    }
                }

                var events = Parse(first, second);
                Check(events.Count == 2, "Valid articles with blank identifiers were dropped.");
                Check(
                    events.Select(item => item.EventId).Distinct(StringComparer.Ordinal).Count() == 2,
                    "Different article URLs collapsed through a blank " + name + "."
                );
                Check(events[0].ContentHash != events[1].ContentHash, "Distinct article content lost its hash.");
            }

            var baseline = Item("https://www.federalreserve.gov/press/id-one.htm");
            baseline.Add(new XElement("id", "opaque-id"));
            var edited = Item("https://www.federalreserve.gov/press/id-two.htm");
            edited.Add(new XElement("guid", blank), new XElement("id", "opaque-id"));
            Check(
                Parse(baseline).Single().EventId == Parse(edited).Single().EventId,
                "Blank GUID must not shadow an available opaque ID."
            );
        }
    }

    private static void ArticleLinks()
    {
        const string article = "https://www.federalreserve.gov/press/article.htm";
        foreach (
            var alternate in new string?[]
            {
                null,
                "alternate",
                "ALTERNATE",
                "AlTeRnAtE",
                "http://www.iana.org/assignments/relation/alternate",
                "HTTP://WWW.IANA.ORG/ASSIGNMENTS/RELATION/ALTERNATE",
            }
        )
        {
            foreach (var relation in new[] { "self", "enclosure" })
            {
                foreach (var leading in new[] { "https://www.federalreserve.gov/feed.xml", "http://127.0.0.1/private" })
                {
                    var events = ParseAtom(Link(leading, relation), Link(article, alternate));
                    Check(
                        events.Count == 1 && events[0].Url == article,
                        "A non-article link replaced or hid the allowed alternate."
                    );
                }
            }

            Check(
                ParseAtom(Link(article, alternate)).Single().Url == article,
                "A standalone supported alternate relation was rejected."
            );
        }

        foreach (var relation in new[] { "self", "SELF", "enclosure", "ENCLOSURE", "related", "via" })
        {
            Check(
                ParseAtom(Link(article, relation)).Count == 0,
                "A non-article relation was used without an alternate: " + relation
            );
        }

        Check(
            ParseAtom(Link(article, "self"), Link("http://127.0.0.1/private", "alternate")).Count == 0,
            "A rejected alternate must not fall back to the allowed self URL."
        );
        Check(
            ParseAtom(Link("https://www.federalreserve.gov.attacker.invalid/article", "alternate")).Count == 0,
            "Alternate selection expanded the existing host policy."
        );
    }

    private static void Compatibility()
    {
        const string firstUrl = "https://www.federalreserve.gov/press/first.htm";
        const string secondUrl = "https://www.federalreserve.gov/press/second.htm";
        foreach (var name in new[] { "guid", "id" })
        {
            foreach (var identifier in new[] { "Opaque-ID", " opaque-id " })
            {
                var first = Item(firstUrl);
                first.Add(new XElement(name, identifier));
                var second = Item(secondUrl);
                second.Add(new XElement(name, identifier));
                Check(
                    Parse(first).Single().EventId == Parse(second).Single().EventId,
                    "A nonblank provider identifier lost priority over the URL."
                );
                var changed = Item(firstUrl);
                changed.Add(new XElement(name, "opaque-id"));
                Check(
                    Parse(first).Single().EventId != Parse(changed).Single().EventId,
                    "Nonblank provider identifier case or whitespace was normalized."
                );
            }
        }

        var rawUrl = Item(" " + firstUrl + " ");
        Check(
            Parse(rawUrl).Single().EventId != Parse(Item(firstUrl)).Single().EventId,
            "Existing raw URL fallback identity was normalized."
        );
        var guidLink = new XElement("item", new XElement("title", "GUID URL fixture"), new XElement("guid", firstUrl));
        Check(Parse(guidLink).Single().Url == firstUrl, "Existing RSS GUID URL fallback was lost.");
        var rssWithMetadata = Item(firstUrl);
        rssWithMetadata.AddFirst(Link("https://www.federalreserve.gov/feed.xml", "self"));
        Check(Parse(rssWithMetadata).Single().Url == firstUrl, "RSS article link was hidden by Atom feed metadata.");
    }

    private static XElement Item(string url) =>
        new("item", new XElement("title", "Identity fixture"), new XElement("link", url));

    private static XElement Link(string url, string? relation) =>
        new("link", new XAttribute("href", url), relation is null ? null : new XAttribute("rel", relation));

    private static IReadOnlyList<NewsEvent> Parse(params XElement[] items) =>
        NewsRssParser.Parse(
            source,
            new XElement("rss", new XElement("channel", items)).ToString(SaveOptions.DisableFormatting),
            fetchedAt
        );

    private static IReadOnlyList<NewsEvent> ParseAtom(params XElement[] links)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var entry = new XElement(
            atom + "entry",
            new XElement(atom + "id", "atom-identity-fixture"),
            new XElement(atom + "title", "Atom identity fixture"),
            links.Select(link => new XElement(atom + "link", link.Attributes()))
        );
        return NewsRssParser.Parse(
            source,
            new XElement(atom + "feed", entry).ToString(SaveOptions.DisableFormatting),
            fetchedAt
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
