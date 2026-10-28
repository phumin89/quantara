using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class NewsEvidenceReader(HttpClient http) : INewsEvidenceReader
{
    public async Task<string> Read(string? symbols, CancellationToken cancellationToken)
    {
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(http.Timeout);
            var query = string.IsNullOrWhiteSpace(symbols) ? "" : "?symbols=" + Uri.EscapeDataString(symbols);
            using var response = await http.GetAsync(
                "api/market/news" + query,
                HttpCompletionOption.ResponseHeadersRead,
                requestTimeout.Token
            );
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(16000, requestTimeout.Token);
            var body = await response.Content.ReadAsStringAsync(requestTimeout.Token);
            if (body.Length > 4000)
            {
                return Unavailable("packet_too_large");
            }

            using var document = JsonDocument.Parse(body);
            var valid = Valid(document.RootElement);
            requestTimeout.Token.ThrowIfCancellationRequested();
            return valid ? body : Unavailable("invalid_packet");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Unavailable("source_unavailable");
        }
        catch (OperationCanceledException)
        {
            return Unavailable("source_unavailable");
        }
        catch (Exception error)
            when (error
                    is HttpRequestException
                        or JsonException
                        or InvalidOperationException
                        or FormatException
                        or ArgumentException
                        or OperationCanceledException
                        or KeyNotFoundException
            )
        {
            return Unavailable("invalid_packet");
        }
    }

    private static bool Valid(JsonElement root)
    {
        var now = DateTimeOffset.UtcNow;
        if (
            root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Count() != 7
            || root.GetProperty("schema").GetString() != "news-v1"
            || root.GetProperty("executionAuthority").ValueKind != JsonValueKind.False
            || !Time(root.GetProperty("observedAt"), out var observed)
            || observed > now.AddSeconds(5)
            || now - observed > TimeSpan.FromMinutes(15)
            || !root.GetProperty("omittedEventCount").TryGetInt32(out var omitted)
            || omitted < 0
        )
        {
            return false;
        }

        var events = root.GetProperty("events");
        var sources = root.GetProperty("sourceStates");
        if (
            events.ValueKind != JsonValueKind.Array
            || events.GetArrayLength() > 5
            || sources.ValueKind != JsonValueKind.Array
            || sources.GetArrayLength() > 8
        )
        {
            return false;
        }

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources.EnumerateArray())
        {
            var id = source.GetProperty("id").GetString();
            if (
                source.EnumerateObject().Count() != 7
                || id is not ("federal-reserve" or "bls" or "bitkub")
                || !sourceIds.Add(id)
                || !Text(source.GetProperty("label"), 200)
                || !Url(source.GetProperty("url"), id)
                || source.GetProperty("status").GetString()
                    is not (
                        "Healthy"
                        or "NoNewEvents"
                        or "Unavailable"
                        or "RateLimited"
                        or "Stale"
                        or "ParseFailed"
                        or "UnsupportedSource"
                    )
                || !NullableTime(source.GetProperty("lastSuccessAt"))
                || !NullableTime(source.GetProperty("nextRetryAt"))
                || source.GetProperty("reason").ValueKind != JsonValueKind.Null
                    && !Text(source.GetProperty("reason"), 500)
            )
            {
                return false;
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in events.EnumerateArray())
        {
            var sourceId = item.GetProperty("sourceId").GetString();
            var id = item.GetProperty("eventId").GetString();
            if (
                item.EnumerateObject().Count() != 15
                || !Token(id)
                || !ids.Add(id ?? "")
                || sourceId is null
                || !sourceIds.Contains(sourceId)
                || !item.GetProperty("revision").TryGetInt32(out var revision)
                || revision < 1
                || !Text(item.GetProperty("title"), 240)
                || !Url(item.GetProperty("url"), sourceId)
                || !Time(item.GetProperty("firstSeenAt"), out var first)
                || !Time(item.GetProperty("revisionSeenAt"), out var revisionAt)
                || !Time(item.GetProperty("fetchedAt"), out var fetched)
                || first > revisionAt
                || revisionAt > observed.AddSeconds(5)
                || fetched > observed.AddSeconds(5)
                || !NullableTime(item.GetProperty("publishedAt"))
                || !NullableTime(item.GetProperty("expiresAt"))
                || item.GetProperty("marketWide").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !Text(item.GetProperty("category"), 80)
                || !Text(item.GetProperty("factStatus"), 80)
                || !Text(item.GetProperty("contentHash"), 128)
            )
            {
                return false;
            }

            if (
                Time(item.GetProperty("publishedAt"), out var published) && published > observed.AddSeconds(5)
                || Time(item.GetProperty("expiresAt"), out var expiry) && expiry <= observed
            )
            {
                return false;
            }

            var symbols = item.GetProperty("symbols");
            if (
                symbols.ValueKind != JsonValueKind.Array
                || symbols.GetArrayLength() > 16
                || symbols.EnumerateArray().Any(symbol => !Token(symbol.GetString()))
            )
            {
                return false;
            }
        }

        var pilot = root.GetProperty("pilot");
        if (
            pilot.EnumerateObject().Count() != 4
            || !NullableTime(pilot.GetProperty("startedAt"))
            || !NullableTime(pilot.GetProperty("lastPollAt"))
            || !pilot.GetProperty("gapCount").TryGetInt32(out var gaps)
            || gaps < 0
        )
        {
            return false;
        }

        return pilot.GetProperty("completed72Hours").ValueKind == JsonValueKind.False
            || pilot.GetProperty("completed72Hours").ValueKind == JsonValueKind.True
                && CompletedPilot(pilot, sources, observed);
    }

    private static bool CompletedPilot(JsonElement pilot, JsonElement sources, DateTimeOffset observed)
    {
        if (
            !Time(pilot.GetProperty("startedAt"), out var started)
            || !Time(pilot.GetProperty("lastPollAt"), out var lastPoll)
            || started > lastPoll
            || observed - started < TimeSpan.FromHours(72)
            || lastPoll > observed.AddSeconds(5)
            || observed - lastPoll > TimeSpan.FromMinutes(10)
        )
        {
            return false;
        }

        foreach (var required in new[] { "federal-reserve", "bls" })
        {
            var source = sources
                .EnumerateArray()
                .SingleOrDefault(item => item.GetProperty("id").GetString() == required);
            if (
                source.ValueKind == JsonValueKind.Undefined
                || source.GetProperty("status").GetString() is not ("Healthy" or "NoNewEvents")
                || !Time(source.GetProperty("lastSuccessAt"), out var success)
                || success > observed.AddSeconds(5)
                || observed - success > TimeSpan.FromMinutes(15)
            )
            {
                return false;
            }
        }

        return sources
            .EnumerateArray()
            .Where(item => item.GetProperty("id").GetString() is not "bitkub")
            .All(item => item.GetProperty("status").GetString() is "Healthy" or "NoNewEvents");
    }

    private static bool Time(JsonElement element, out DateTimeOffset value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = element.GetString();
        return text is { Length: >= 20 }
            && (text[^1] == 'Z' || text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':')
            && element.TryGetDateTimeOffset(out value);
    }

    private static bool NullableTime(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null || Time(element, out _);

    private static bool Text(JsonElement element, int maximum) =>
        element.ValueKind == JsonValueKind.String
        && element.GetString() is { } text
        && !string.IsNullOrWhiteSpace(text)
        && text.Length <= maximum;

    private static bool Token(string? value) =>
        value is { Length: > 0 and <= 128 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static bool Url(JsonElement element, string source) =>
        element.ValueKind == JsonValueKind.String
        && Uri.TryCreate(element.GetString(), UriKind.Absolute, out var url)
        && url.Scheme == "https"
        && url.Port == 443
        && string.IsNullOrEmpty(url.UserInfo)
        && (
            source switch
            {
                "federal-reserve" => url.Host is "www.federalreserve.gov" or "federalreserve.gov",
                "bls" => url.Host is "www.bls.gov" or "bls.gov",
                "bitkub" => url.Host == "support.bitkub.com",
                _ => false,
            }
        );

    private static string Unavailable(string reason) =>
        JsonSerializer.Serialize(
            new
            {
                schema = "news-v1",
                observedAt = DateTimeOffset.UtcNow,
                executionAuthority = false,
                events = Array.Empty<object>(),
                sourceStates = new[]
                {
                    new
                    {
                        id = "market-data",
                        label = "News service",
                        url = "https://market-data.invalid",
                        status = "Unavailable",
                        lastSuccessAt = (DateTimeOffset?)null,
                        nextRetryAt = (DateTimeOffset?)null,
                        reason,
                    },
                },
                omittedEventCount = 0,
                pilot = new
                {
                    startedAt = (DateTimeOffset?)null,
                    lastPollAt = (DateTimeOffset?)null,
                    completed72Hours = false,
                    gapCount = 0,
                },
            }
        );
}
