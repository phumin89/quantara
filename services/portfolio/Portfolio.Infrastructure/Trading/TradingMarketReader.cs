using System.Net.Http.Json;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingMarketReader(HttpClient http) : ITradingMarketReader
{
    public async Task<TradingUniverse> ReadUniverse(CancellationToken cancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(http.Timeout);
        using var response = await http.GetAsync(
            "api/market/trading/universe",
            HttpCompletionOption.ResponseHeadersRead,
            requestTimeout.Token
        );
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_048_576, requestTimeout.Token);
        var universe =
            await response.Content.ReadFromJsonAsync<TradingUniverse>(TradingJson.Options, requestTimeout.Token)
            ?? throw new InvalidDataException("Market universe is unavailable.");
        if (
            universe.Symbols.Length > 1000
            || universe.TotalMarkets < universe.Symbols.Length
            || universe.TotalMarkets > 1000
            || universe.ExcludedMarkets < 0
            || universe.Symbols.Distinct(StringComparer.Ordinal).Count() != universe.Symbols.Length
            || universe.Symbols.Any(symbol =>
                symbol.Length is < 1 or > 30
                || symbol.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            )
        )
        {
            throw new InvalidDataException("Invalid market universe identities or coverage.");
        }

        requestTimeout.Token.ThrowIfCancellationRequested();
        return universe;
    }

    public async Task<IReadOnlyList<TradingBook>> Read(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    ) => (await this.ReadEvidence(symbols, cancellationToken)).Books;

    public Task<TradingMarketEvidence> ReadEvidence(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken,
        bool adaptiveTimeframes = false
    ) => this.ReadBooks(symbols, true, cancellationToken, adaptiveTimeframes);

    public async Task<IReadOnlyList<TradingBook>> ReadValuations(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    ) => (await this.ReadBooks(symbols, false, cancellationToken)).Books;

    private async Task<TradingMarketEvidence> ReadBooks(
        IReadOnlyList<string> symbols,
        bool history,
        CancellationToken cancellationToken,
        bool adaptiveTimeframes = false
    )
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(http.Timeout);
        using var response = await http.GetAsync(
            "api/market/trading?symbols="
                + Uri.EscapeDataString(string.Join(',', symbols))
                + (history ? "" : "&includeHistory=false")
                + (adaptiveTimeframes && history ? "&adaptiveTimeframes=true" : ""),
            HttpCompletionOption.ResponseHeadersRead,
            requestTimeout.Token
        );
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(4_000_000, requestTimeout.Token);
        var text = await response.Content.ReadAsStringAsync(requestTimeout.Token);
        if (text.Length > 1000000)
        {
            throw new InvalidDataException("Trading evidence exceeds its budget.");
        }

        using var document = JsonDocument.Parse(text);
        var rows = document.RootElement.GetProperty("markets");
        foreach (var row in rows.EnumerateArray())
        {
            foreach (
                var name in new[] { "minimumNotional", "priceStep", "priceScale", "baseScale", "quoteScale", "enabled" }
            )
            {
                if (!row.TryGetProperty(name, out _))
                {
                    throw new InvalidDataException("Required trading market rules are missing.");
                }
            }
        }

        var books =
            rows.Deserialize<TradingBook[]>(TradingJson.Options)
            ?? throw new InvalidDataException("Trading evidence is unavailable.");
        if (
            books.Any(book =>
                !TradingTimeframes.Supported(book.TimeframeSeconds)
                || !adaptiveTimeframes && history && book.TimeframeSeconds != 300
            )
        )
        {
            throw new InvalidDataException("Unsupported execution timeframe.");
        }

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in document.RootElement.GetProperty("errors").EnumerateArray())
        {
            var symbol = row.GetProperty("symbol").GetString();
            var reason = row.GetProperty("error").GetString();
            if (symbol is null || reason is null || reason.Length is < 1 or > 200 || !errors.TryAdd(symbol, reason))
            {
                throw new InvalidDataException("Invalid market evidence error identity or reason.");
            }
        }

        var identities = books.Select(book => book.Symbol).Concat(errors.Keys).ToArray();
        if (
            identities.Length != symbols.Count
            || identities.Distinct(StringComparer.Ordinal).Count() != identities.Length
            || identities.Any(symbol => !symbols.Contains(symbol, StringComparer.Ordinal))
        )
        {
            throw new InvalidDataException("Unexpected or duplicate market evidence identity.");
        }

        var evidence =
            adaptiveTimeframes && document.RootElement.TryGetProperty("evidence", out var quality)
                ? quality.Deserialize<TradingHistoryEvidence[]>(TradingJson.Options) ?? []
                : [];
        if (
            evidence.Length > symbols.Count * 3
            || evidence.Select(item => (item.Symbol, item.TimeframeSeconds)).Distinct().Count() != evidence.Length
            || evidence.Any(item =>
                !symbols.Contains(item.Symbol, StringComparer.Ordinal)
                || !TradingTimeframes.Supported(item.TimeframeSeconds)
                || item.Source != "bitkub-tradingview"
                || item.Status is not ("ready" or "unavailable")
                || item.GapCause is not ("none" or "unknown" or "source-unavailable")
                || item.CompletedBars is < 0 or > 120
                || item.ContiguousBars is < 0 or > 120
                || item.MissingIntervals is < 0 or > 120
                || item.ContiguousBars > item.CompletedBars
                || item.VerifiedZeroVolumeBars < 0
                || item.VerifiedZeroVolumeBars > item.CompletedBars
                || item.CompletedBars + item.MissingIntervals != 120
                || item.Status == "unavailable" && string.IsNullOrWhiteSpace(item.Reason)
                || item.Status == "ready" && item.ContiguousBars < 51
                || item.Reason is { Length: > 200 }
            )
        )
        {
            throw new InvalidDataException("Invalid native history provenance or readiness.");
        }

        if (
            adaptiveTimeframes
            && books.Any(book =>
                !evidence.Any(item =>
                    item.Symbol == book.Symbol
                    && item.TimeframeSeconds == book.TimeframeSeconds
                    && item.Status == "ready"
                    && item.ContiguousBars == book.Bars.Count
                    && item.CheckedAt >= book.ObservedAt
                    && item.CheckedAt - book.ObservedAt < TimeSpan.FromSeconds(90)
                )
            )
        )
        {
            throw new InvalidDataException("Native execution history is missing its readiness evidence.");
        }

        requestTimeout.Token.ThrowIfCancellationRequested();
        return new(books, errors, evidence);
    }
}
