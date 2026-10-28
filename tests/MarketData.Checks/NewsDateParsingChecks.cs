using System.Globalization;
using System.Xml.Linq;
using Quantara.MarketData.Domain.News;
using Quantara.MarketData.Infrastructure.News;

namespace Quantara.MarketData.Checks;

public static class NewsDateParsingChecks
{
    public static void Run()
    {
        var source = new NewsSource("date-fixture", "Date fixture", new Uri("https://www.federalreserve.gov/feed.xml"));
        var fetchedAt = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var cultureName in new[] { "en-US", "en-GB", "th-TH", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                Accepted(source, fetchedAt);
                Unknown(source, fetchedAt);
                Compatibility(fetchedAt);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        Console.WriteLine(
            "News date parsing checks passed: invariant ISO/RFC zones, fractional seconds, culture independence and unknown expiry."
        );
    }

    private static void Accepted(NewsSource source, DateTimeOffset fetchedAt)
    {
        var cases = new[]
        {
            ("Sun, 18 Oct 2026 10:00:00 GMT", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            ("Sun, 18 Oct 2026 10:00:00 UT", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            ("Sun, 18 Oct 2026 10:00:00 EST", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-5))),
            ("Sun, 18 Oct 2026 10:00:00 EDT", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-4))),
            ("Sun, 18 Oct 2026 10:00:00 CST", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-6))),
            ("Sun, 18 Oct 2026 10:00:00 CDT", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-5))),
            ("Sun, 18 Oct 2026 10:00:00 MST", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-7))),
            ("Sun, 18 Oct 2026 10:00:00 MDT", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-6))),
            ("Sun, 18 Oct 2026 10:00:00 PST", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-8))),
            ("Sun, 18 Oct 2026 10:00:00 PDT", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-7))),
            ("Sun, 18 Oct 2026 10:00:00 +0700", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(7))),
            ("Sun, 18 Oct 2026 10:00:00 +07:00", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(7))),
            ("2026-10-18T10:00:00Z", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            ("2026-10-18T10:00:00+07:00", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(7))),
            ("2026-10-18T10:00:00+0700", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(7))),
            ("2026-10-18T10:00:00-05:30", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-5.5))),
            ("18 Oct 2026 10:00:00 +0000", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            ("18 Oct 2026 10:00 UT", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            ("9 Oct 2026 10:00 GMT", new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)),
            (
                "2026-10-18T10:00:00.1234567+07:00",
                new DateTimeOffset(
                    new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Unspecified).AddTicks(1234567),
                    TimeSpan.FromHours(7)
                )
            ),
            ("2026-10-18t10:00:00z", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            ("Sun, 18 Oct 26 10:00:00 GMT", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            ("18 Oct 49 10:00:00 GMT", new DateTimeOffset(2049, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            ("18 Oct 50 10:00:00 GMT", new DateTimeOffset(1950, 10, 18, 10, 0, 0, TimeSpan.Zero)),
        };

        foreach (var (value, expected) in cases)
        {
            var news = Parse(source, value, fetchedAt).Single();
            Check(
                news.PublishedAt is { } published && published == expected && published.Offset == expected.Offset,
                "Accepted date changed instant or exact offset: " + value
            );
            Check(
                news.ExpiresAt is { } expiry && expiry == expected.AddDays(7) && expiry.Offset == expected.Offset,
                "Accepted publication uses seven-day expiry: " + value
            );
        }
    }

    private static void Unknown(NewsSource source, DateTimeOffset fetchedAt)
    {
        foreach (
            var value in new[]
            {
                "10:00:00 GMT",
                "01/02/2026 03:04:05 +0000",
                "2026-10-18T10:00:00",
                "Sun, 18 Oct 2026 10:00:00",
                "10:00:00",
                "2026-10-18",
                "Sun, 18 Oct 2026 10:00:00 XYZ",
                "2026-10-18T10:00:00+25:00",
                "not-a-date",
                "2026-13-40T10:00:00Z",
                "2026-02-30T10:00:00Z",
                "",
                new string('x', 129),
            }
        )
        {
            var news = Parse(source, value, fetchedAt).Single();
            Check(news.PublishedAt is null, "Locale-ambiguous or incomplete date was accepted: " + value);
            Check(
                news.ExpiresAt is { } expiry && expiry == fetchedAt.AddDays(1) && expiry.Offset == fetchedAt.Offset,
                "Unknown publication uses one-day expiry: " + value
            );
        }
    }

    private static void Compatibility(DateTimeOffset fetchedAt)
    {
        var source = new NewsSource("fed", "Federal Reserve", new Uri("https://www.federalreserve.gov/feed.xml"));
        var news = ParseRaw(
                source,
                "Rate decision",
                "https://www.federalreserve.gov/press/a.xml",
                null,
                "Sun, 18 Oct 2026 10:00:00 GMT",
                fetchedAt
            )
            .Single();
        Check(news.EventId == "EAC4B18AFD4354B7E7D2F3213AB063F9", "Existing RSS event identity changed.");
        Check(
            news.ContentHash == "4A3E32F7C6BA09BE40F919DF863EA766DB16D1B51879568C29D3F0D78A57448F",
            "Existing RSS content hash changed."
        );
    }

    private static IReadOnlyList<NewsEvent> Parse(NewsSource source, string value, DateTimeOffset fetchedAt)
    {
        return ParseRaw(
            source,
            "Date fixture",
            "https://www.federalreserve.gov/press/date-fixture.xml",
            "date-fixture",
            value,
            fetchedAt
        );
    }

    private static IReadOnlyList<NewsEvent> ParseRaw(
        NewsSource source,
        string title,
        string link,
        string? guid,
        string value,
        DateTimeOffset fetchedAt
    )
    {
        var item = new XElement(
            "item",
            new XElement("title", title),
            new XElement("link", link),
            guid is null ? null : new XElement("guid", guid),
            new XElement("pubDate", value)
        );
        var body = new XElement("rss", new XElement("channel", item)).ToString(SaveOptions.DisableFormatting);
        return NewsRssParser.Parse(source, body, fetchedAt);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
