using System.Net.Http.Json;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingMarketReader(HttpClient http) : ITradingMarketReader
{
    public async Task<TradingUniverse> ReadUniverse(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("api/market/trading/universe", cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_048_576, cancellationToken);
        var universe =
            await response.Content.ReadFromJsonAsync<TradingUniverse>(TradingJson.Options, cancellationToken)
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

        return universe;
    }

    public async Task<IReadOnlyList<TradingBook>> Read(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    ) => await this.ReadBooks(symbols, true, cancellationToken);

    public Task<IReadOnlyList<TradingBook>> ReadValuations(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    ) => this.ReadBooks(symbols, false, cancellationToken);

    private async Task<IReadOnlyList<TradingBook>> ReadBooks(
        IReadOnlyList<string> symbols,
        bool history,
        CancellationToken cancellationToken
    )
    {
        using var response = await http.GetAsync(
            "api/market/trading?symbols="
                + Uri.EscapeDataString(string.Join(',', symbols))
                + (history ? "" : "&includeHistory=false"),
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
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

        return rows.Deserialize<TradingBook[]>(TradingJson.Options)
            ?? throw new InvalidDataException("Trading evidence is unavailable.");
    }
}
