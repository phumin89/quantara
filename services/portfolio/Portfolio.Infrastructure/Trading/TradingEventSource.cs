using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingEventSource(HttpClient http, string token, TimeSpan? heartbeatTimeout = null)
    : ITradingEventSource
{
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter<TradingEventKind>(JsonNamingPolicy.CamelCase, false) },
    };

    public async IAsyncEnumerable<TradingMarketEvent> Read(
        IReadOnlyList<string> required,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(token)
            || required.Count > 64
            || required.Any(symbol => !ValidSymbol(symbol))
            || required.Distinct(StringComparer.Ordinal).Count() != required.Count
        )
        {
            throw new InvalidDataException("Market stream configuration is unavailable.");
        }

        using var watch = new HttpRequestMessage(HttpMethod.Post, "api/market/events/watch")
        {
            Content = JsonContent.Create(new { symbols = required }),
        };
        watch.Headers.Add("X-Quantara-Market-Events", token);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshake.CancelAfter(TimeSpan.FromSeconds(20));
        using var updated = await http.SendAsync(watch, HttpCompletionOption.ResponseHeadersRead, handshake.Token);
        updated.EnsureSuccessStatusCode();
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/market/events");
        request.Headers.Add("X-Quantara-Market-Events", token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, handshake.Token);
        response.EnsureSuccessStatusCode();
        handshake.CancelAfter(Timeout.InfiniteTimeSpan);
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
        {
            throw new InvalidDataException("Market event stream has an invalid content type.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var lines = new BoundedSseReader(reader);
        var seen = new Dictionary<(string, TradingEventKind), long>();
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(heartbeatTimeout ?? TimeSpan.FromSeconds(45));
        while (true)
        {
            var line = await lines.Read(idle.Token);
            if (line is null)
            {
                yield break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var item =
                JsonSerializer.Deserialize<TradingMarketEvent>(line[5..].Trim(), json)
                ?? throw new InvalidDataException("Invalid market event.");
            Validate(item, DateTimeOffset.UtcNow);
            idle.CancelAfter(heartbeatTimeout ?? TimeSpan.FromSeconds(45));
            if (item.Kind != TradingEventKind.Heartbeat)
            {
                var key = (item.Symbol, item.Kind);
                if (seen.TryGetValue(key, out var sequence) && sequence >= item.Sequence)
                {
                    continue;
                }

                if (seen.Count >= 320 && !seen.ContainsKey(key))
                {
                    seen.Clear();
                }

                seen[key] = item.Sequence;
            }

            yield return item;
        }
    }

    private static void Validate(TradingMarketEvent item, DateTimeOffset now)
    {
        if (
            item.Kind is < TradingEventKind.QuoteChanged or > TradingEventKind.Heartbeat
            || item.ObservedAt < now.AddSeconds(-45)
            || item.ObservedAt > now.AddSeconds(5)
            || item.WatchedSymbols.Count > 64
            || item.WatchedSymbols.Any(symbol => !ValidSymbol(symbol))
            || item.WatchedSymbols.Distinct(StringComparer.Ordinal).Count() != item.WatchedSymbols.Count
            || item.Coalesced < 0
            || item.Sequence < 0
            || (item.Kind != TradingEventKind.Heartbeat && (!ValidSymbol(item.Symbol) || item.Sequence == 0))
            || (item.Kind == TradingEventKind.Heartbeat && (item.Symbol.Length != 0 || item.Book is not null))
            || (
                item.Kind == TradingEventKind.CandleClosed
                && (
                    item.CandleAt is not { } candle
                    || candle % 300 != 0
                    || candle > now.ToUnixTimeSeconds() / 300 * 300 - 300
                )
            )
        )
        {
            throw new InvalidDataException("Invalid market event identity, timestamp or coverage.");
        }

        if (
            item.Book is { } book
            && (
                book.Symbol != item.Symbol
                || book.ObservedAt > item.ObservedAt
                || now - book.ObservedAt > TimeSpan.FromSeconds(90)
                || book.Bid <= 0
                || book.Ask < book.Bid
                || book.BidQuantity <= 0
                || book.AskQuantity <= 0
                || book.Bars.Count != 0
                || book.MinimumNotional <= 0
                || book.PriceStep <= 0
                || book.PriceScale is < 0 or > 18
                || book.BaseScale is < 0 or > 18
                || book.QuoteScale is < 0 or > 18
                || decimal.Round(book.PriceStep, book.PriceScale) != book.PriceStep
            )
        )
        {
            throw new InvalidDataException("Invalid executable event book.");
        }
    }

    private static bool ValidSymbol(string symbol) =>
        symbol.Length is > 0 and <= 30 && symbol.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');
}
