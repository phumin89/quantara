using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class CompanyResearchSignalReader(
    MarketSignalReader market,
    INewsEvidenceReader news,
    ICurrentAccountReader account,
    TimeProvider time
) : IMarketSignalReader
{
    public async Task<MarketSignalNotice?> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MarketSignalNotice? marketNotice = null;
        try
        {
            marketNotice = await market.Read(cancellationToken);
        }
        catch (Exception error)
            when (!cancellationToken.IsCancellationRequested
                && error
                    is HttpRequestException
                        or JsonException
                        or InvalidDataException
                        or TaskCanceledException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or FormatException
            ) { }

        var newsEvents = new List<(string Key, DateTimeOffset Seen, DateTimeOffset Published, int Revision)>();
        try
        {
            using var document = JsonDocument.Parse(await news.Read(null, cancellationToken));
            if (document.RootElement.TryGetProperty("events", out var events))
            {
                if (events.GetArrayLength() > 5)
                {
                    throw new InvalidDataException("News research event count exceeds its budget.");
                }

                foreach (var item in events.EnumerateArray())
                {
                    var seen = item.GetProperty("revisionSeenAt").GetDateTimeOffset();
                    var published =
                        item.TryGetProperty("publishedAt", out var publishedElement)
                        && publishedElement.ValueKind == JsonValueKind.String
                            ? publishedElement.GetDateTimeOffset()
                            : item.GetProperty("firstSeenAt").GetDateTimeOffset();
                    var revision = item.GetProperty("revision").GetInt32();
                    newsEvents.Add(
                        (item.GetProperty("eventId").GetString() + "@" + revision, seen, published, revision)
                    );
                }
            }
        }
        catch (Exception error)
            when (!cancellationToken.IsCancellationRequested
                && error
                    is HttpRequestException
                        or JsonException
                        or InvalidDataException
                        or TaskCanceledException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or FormatException
            ) { }

        cancellationToken.ThrowIfCancellationRequested();
        var now = time.GetUtcNow();
        marketNotice = FreshMarket(marketNotice, now);
        var newsIds = FreshNewsKeys(newsEvents, now);
        if (marketNotice is null && newsIds.Length == 0)
        {
            return null;
        }

        var accountKey = await account.Read(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        now = time.GetUtcNow();
        marketNotice = FreshMarket(marketNotice, now);
        newsIds = FreshNewsKeys(newsEvents, now);
        if (marketNotice is null && newsIds.Length == 0)
        {
            return null;
        }

        var parts = newsIds
            .Order(StringComparer.Ordinal)
            .Concat(marketNotice is null ? [] : [marketNotice.Key])
            .Append(accountKey);
        var key = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))))
            .ToLowerInvariant();
        var description = marketNotice?.Description ?? "Fresh news research event; no market signal available.";
        if (newsIds.Length > 0)
        {
            description += $" {newsIds.Length} fresh news event(s) available for research.";
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new MarketSignalNotice(
            key,
            description[..Math.Min(500, description.Length)],
            marketNotice?.CheckedAt ?? now
        );
    }

    private static MarketSignalNotice? FreshMarket(MarketSignalNotice? notice, DateTimeOffset now) =>
        notice is not null && notice.CheckedAt <= now.AddSeconds(5) && now - notice.CheckedAt <= TimeSpan.FromMinutes(2)
            ? notice
            : null;

    private static string[] FreshNewsKeys(
        IReadOnlyList<(string Key, DateTimeOffset Seen, DateTimeOffset Published, int Revision)> events,
        DateTimeOffset now
    ) =>
        events
            .Where(item =>
                item.Seen <= now
                && now - item.Seen <= TimeSpan.FromMinutes(15)
                && item.Published <= now
                && (item.Revision > 1 || now - item.Published <= TimeSpan.FromMinutes(15))
            )
            .Select(item => item.Key)
            .ToArray();
}
