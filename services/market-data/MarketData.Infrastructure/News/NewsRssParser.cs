using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Quantara.MarketData.Domain.News;

namespace Quantara.MarketData.Infrastructure.News;

public sealed class NewsRssParser
{
    private static readonly string[] isoDateFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    private static readonly string[] rssDateFormats =
    [
        "ddd, d MMM yyyy HH:mm:ss zzz",
        "d MMM yyyy HH:mm:ss zzz",
        "ddd, d MMM yyyy HH:mm zzz",
        "d MMM yyyy HH:mm zzz",
    ];

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
            var link = item.Elements()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "link"
                    && element.Attribute("rel")?.Value.ToLowerInvariant()
                        is null
                            or "alternate"
                            or "http://www.iana.org/assignments/relation/alternate"
                );
            var url = link?.Attribute("href")?.Value ?? link?.Value ?? Value(item, "guid");
            var providerId = Value(item, "guid");
            if (string.IsNullOrWhiteSpace(providerId))
            {
                providerId = Value(item, "id");
            }

            if (string.IsNullOrWhiteSpace(providerId))
            {
                providerId = url;
            }

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
            DateTimeOffset expiresAt;
            try
            {
                expiresAt = published?.AddDays(7) ?? fetchedAt.AddDays(1);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidDataException("News publication time cannot form a valid expiry.");
            }

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

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length >= 5 && normalized[^5] is '+' or '-' && normalized[^4..].All(char.IsAsciiDigit))
        {
            normalized = normalized.Insert(normalized.Length - 2, ":");
        }

        // Literal Z supplies UTC; the other formats retain their explicit numeric offset.
        if (
            DateTimeOffset.TryParseExact(
                normalized,
                NewsRssParser.isoDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed
            )
        )
        {
            return parsed;
        }

        var parts = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (5 or 6))
        {
            return null;
        }

        var yearIndex = parts.Length - 3;
        if (parts[yearIndex].Length == 2 && parts[yearIndex].All(char.IsAsciiDigit))
        {
            var year = int.Parse(parts[yearIndex], CultureInfo.InvariantCulture);
            parts[yearIndex] = (year < 50 ? year + 2000 : year + 1900).ToString("0000", CultureInfo.InvariantCulture);
        }

        parts[^1] = parts[^1] switch
        {
            "GMT" or "UT" or "UTC" or "Z" => "+00:00",
            "EST" or "CDT" => "-05:00",
            "EDT" => "-04:00",
            "CST" or "MDT" => "-06:00",
            "MST" or "PDT" => "-07:00",
            "PST" => "-08:00",
            _ => parts[^1],
        };

        return DateTimeOffset.TryParseExact(
            string.Join(' ', parts),
            NewsRssParser.rssDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed
        )
            ? parsed
            : null;
    }
}
