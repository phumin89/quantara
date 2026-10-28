using System.Globalization;
using System.Text.Json;

namespace Quantara.MarketData.Infrastructure;

public static class MarketWatchSelection
{
    public static IReadOnlyList<MarketWatchSymbol> Select(
        JsonElement symbols,
        JsonElement tickers,
        IReadOnlyList<string> required,
        DateTimeOffset now
    )
    {
        ValidateRequired(required);
        var scan = MarketUniverseScanner.Scan(symbols, tickers);
        var entries = scan.Entries.ToDictionary(entry => entry.Symbol, StringComparer.Ordinal);
        var selected = new List<MarketWatchSymbol>();
        var rawRules = symbols
            .GetProperty("result")
            .EnumerateArray()
            .Where(MarketUniverseScanner.IsEligibleRule)
            .GroupBy(rule => MarketUniverseScanner.Text(rule, "base_asset"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.DistinctBy(rule => rule.GetRawText()).ToArray(),
                StringComparer.OrdinalIgnoreCase
            );
        foreach (var baseSymbol in required)
        {
            if (!rawRules.TryGetValue(baseSymbol, out var rules) || rules.Length != 1)
            {
                continue;
            }

            if (
                int.TryParse(
                    MarketUniverseScanner.Text(rules[0], "pairing_id"),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var pairingId
                )
                && pairingId > 0
            )
            {
                var volume = entries.TryGetValue(baseSymbol + "_THB", out var entry) ? entry.Volume24h ?? 0m : 0m;
                selected.Add(new MarketWatchSymbol(baseSymbol + "_THB", pairingId, volume));
            }
        }

        var eligible = scan
            .Entries.Where(entry =>
                entry.Usable && entry.Volume24h is > 0m && !selected.Any(item => item.Symbol == entry.Symbol)
            )
            .OrderByDescending(entry => entry.Volume24h)
            .ThenBy(entry => entry.Symbol, StringComparer.Ordinal)
            .Select(ToWatchSymbol)
            .OfType<MarketWatchSymbol>()
            .ToArray();
        var top = eligible.Take(8).ToArray();
        var rotating = eligible.Skip(8).ToArray();
        var rotation = rotating.Length == 0 ? 0 : (int)((now.ToUnixTimeSeconds() / 300 * 8) % rotating.Length);
        var candidateLimit = Math.Min(16, 64 - selected.Count);
        for (var index = 0; index < Math.Min(candidateLimit, top.Length); index++)
        {
            var item = top[index];
            if (!selected.Any(existing => existing.Symbol == item.Symbol))
            {
                selected.Add(item);
            }
        }

        var remaining = Math.Min(8, Math.Max(0, candidateLimit - top.Length));
        for (var index = 0; index < remaining && index < rotating.Length; index++)
        {
            var item = rotating[(rotation + index) % rotating.Length];
            if (!selected.Any(existing => existing.Symbol == item.Symbol))
            {
                selected.Add(item);
            }
        }

        return selected;
    }

    private static MarketWatchSymbol? ToWatchSymbol(MarketScanEntry entry) =>
        int.TryParse(entry.PairingId, NumberStyles.None, CultureInfo.InvariantCulture, out var pairingId)
        && pairingId > 0
            ? new MarketWatchSymbol(entry.Symbol, pairingId, entry.Volume24h ?? 0m)
            : null;

    private static void ValidateRequired(IReadOnlyList<string> required)
    {
        if (
            required.Count > 64
            || required.Any(symbol =>
                symbol.Length is < 1 or > 30
                || !string.Equals(symbol, symbol.ToUpperInvariant(), StringComparison.Ordinal)
                || symbol.Any(character => !char.IsAsciiLetterOrDigit(character))
            )
            || required.Distinct(StringComparer.Ordinal).Count() != required.Count
        )
        {
            throw new ArgumentException(
                "Required symbols must be unique uppercase ASCII base symbols.",
                nameof(required)
            );
        }
    }
}
