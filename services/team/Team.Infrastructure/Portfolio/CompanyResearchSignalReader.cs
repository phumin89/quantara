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
        var now = time.GetUtcNow();
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

        var newsIds = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(await news.Read(null, cancellationToken));
            if (document.RootElement.TryGetProperty("events", out var events))
            {
                foreach (var item in events.EnumerateArray())
                {
                    var seen = item.GetProperty("revisionSeenAt").GetDateTimeOffset();
                    var published =
                        item.TryGetProperty("publishedAt", out var publishedElement)
                        && publishedElement.ValueKind == JsonValueKind.String
                            ? publishedElement.GetDateTimeOffset()
                            : item.GetProperty("firstSeenAt").GetDateTimeOffset();
                    if (
                        seen <= now
                        && now - seen <= TimeSpan.FromMinutes(15)
                        && published <= now
                        && (item.GetProperty("revision").GetInt32() > 1 || now - published <= TimeSpan.FromMinutes(15))
                    )
                    {
                        newsIds.Add(
                            item.GetProperty("eventId").GetString() + "@" + item.GetProperty("revision").GetInt32()
                        );
                    }
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

        if (marketNotice is null && newsIds.Count == 0)
        {
            return null;
        }

        var accountKey = await account.Read(cancellationToken);
        var parts = newsIds
            .Order(StringComparer.Ordinal)
            .Concat(marketNotice is null ? [] : [marketNotice.Key])
            .Append(accountKey);
        var key = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))))
            .ToLowerInvariant();
        var description = marketNotice?.Description ?? "Fresh news research event; no market signal available.";
        if (newsIds.Count > 0)
        {
            description += $" {newsIds.Count} fresh news event(s) available for research.";
        }

        return new MarketSignalNotice(key, description[..Math.Min(500, description.Length)], now);
    }
}
