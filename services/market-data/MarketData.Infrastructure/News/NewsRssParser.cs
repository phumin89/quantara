using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Quantara.MarketData.Domain.News;

namespace Quantara.MarketData.Infrastructure.News;

public sealed class NewsRssParser
{
    public static IReadOnlyList<NewsEvent> Parse(NewsSource source, string body, DateTimeOffset fetchedAt)
    {
        if (body.Length > 1_000_000)
        {
            throw new InvalidDataException("News response is too large.");
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 1_000_000,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(new StringReader(body), settings);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name.LocalName is not ("rss" or "feed" or "RDF"))
        {
            throw new InvalidDataException("The provider did not return a supported news feed.");
        }

        var items = document.Descendants().Where(element => element.Name.LocalName is "item" or "entry");
        var result = new List<NewsEvent>();
        foreach (var item in items.Take(100))
        {
            var title = Value(item, "title");
            var link = item.Elements().FirstOrDefault(element => element.Name.LocalName == "link");
            var url = link?.Attribute("href")?.Value ?? link?.Value ?? Value(item, "guid");
            var providerId = Value(item, "guid") ?? Value(item, "id") ?? url;
            if (
                string.IsNullOrWhiteSpace(title)
                || title.Length > 240
                || !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || !NewsNetworkPolicy.IsAllowedLink(source.Url, parsed)
            )
            {
                continue;
            }

            var published = ParseDate(Value(item, "pubDate") ?? Value(item, "published") ?? Value(item, "updated"));
            var eventId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source.Id}|{providerId}")))[
                ..32
            ];
            var hash = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes($"{title.Trim()}|{parsed}|{published:O}|{Value(item, "updated")}")
                )
            );
            var expiresAt = published?.AddDays(7) ?? fetchedAt.AddDays(1);
            result.Add(
                new NewsEvent(
                    eventId,
                    1,
                    source.Id,
                    title.Trim(),
                    parsed.ToString(),
                    published,
                    fetchedAt,
                    fetchedAt,
                    fetchedAt,
                    expiresAt,
                    Array.Empty<string>(),
                    true,
                    "macro",
                    "FirstPartyClaim",
                    hash
                )
            );
        }

        return result;
    }

    private static string? Value(XElement item, string name) =>
        item.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
