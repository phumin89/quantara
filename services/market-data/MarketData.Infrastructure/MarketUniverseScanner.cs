using System.Globalization;
using System.Text.Json;

namespace Quantara.MarketData.Infrastructure;

public static class MarketUniverseScanner
{
    public static MarketScanResult Scan(JsonElement symbols, JsonElement ticker)
    {
        var ruleGroups = symbols
            .GetProperty("result")
            .EnumerateArray()
            .Where(MarketUniverseScanner.IsEligibleRule)
            .GroupBy(item => MarketUniverseScanner.Text(item, "base_asset"), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var conflictingRules = ruleGroups
            .Where(group => group.Select(item => item.GetRawText()).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rules = ruleGroups.Select(group => group.First()).ToArray();
        var quotes = ticker
            .EnumerateArray()
            .Where(item => MarketUniverseScanner.Text(item, "symbol").EndsWith("_THB", StringComparison.Ordinal))
            .GroupBy(item => MarketUniverseScanner.Text(item, "symbol"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var entries = new List<MarketScanEntry>();
        var missing = 0;
        var excluded = 0;
        var conflicts = conflictingRules.Count;
        foreach (var rule in rules.OrderBy(item => MarketUniverseScanner.Text(item, "base_asset")))
        {
            var symbol = MarketUniverseScanner.Text(rule, "base_asset").ToUpperInvariant() + "_THB";
            var pairingId = MarketUniverseScanner.Text(rule, "pairing_id");
            if (conflictingRules.Contains(MarketUniverseScanner.Text(rule, "base_asset")))
            {
                entries.Add(new(symbol, pairingId, null, null, null, null, null, false, rule.Clone(), null));
                continue;
            }

            var validPair =
                int.TryParse(pairingId, NumberStyles.None, CultureInfo.InvariantCulture, out var pairing)
                && pairing > 0;
            if (!validPair)
            {
                excluded += 1;
            }

            if (!quotes.TryGetValue(symbol, out var matches) || matches.Length == 0)
            {
                missing += 1;
                entries.Add(new(symbol, pairingId, null, null, null, null, null, false, rule.Clone(), null));
                continue;
            }

            if (matches.Length > 1 && !MarketUniverseScanner.EquivalentQuotes(matches))
            {
                conflicts += 1;
                entries.Add(new(symbol, pairingId, null, null, null, null, null, false, rule.Clone(), null));
                continue;
            }

            var quote = matches[0];
            var last = MarketUniverseScanner.OptionalNumber(quote, "last");
            var bid = MarketUniverseScanner.OptionalNumber(quote, "highest_bid");
            var ask = MarketUniverseScanner.OptionalNumber(quote, "lowest_ask");
            var high = MarketUniverseScanner.OptionalNumber(quote, "high_24_hr");
            var low = MarketUniverseScanner.OptionalNumber(quote, "low_24_hr");
            var change = MarketUniverseScanner.OptionalNumber(quote, "percent_change");
            var volume = MarketUniverseScanner.OptionalNumber(quote, "quote_volume");
            decimal? range = null;
            decimal? spread = null;
            try
            {
                range = last is > 0 && high is not null && low is not null ? (high - low) / last * 100 : null;
                spread = bid is > 0 && ask is > 0 ? (ask - bid) / ((ask + bid) / 2) * 100 : null;
            }
            catch (OverflowException)
            {
                // Unrepresentable metrics invalidate this quote, not the other markets in the scan.
                range = null;
                spread = null;
            }

            var usable =
                validPair
                && last is > 0
                && bid is > 0
                && ask >= bid
                && volume is >= 0
                && change is not null
                && high >= last
                && low is > 0
                && low <= last
                && range is >= 0
                && spread is >= 0;
            entries.Add(
                new(symbol, pairingId, last, change, range, volume, spread, usable, rule.Clone(), quote.Clone())
            );
        }

        return new(entries, rules.Length, missing, excluded, conflicts);
    }

    public static IReadOnlyList<MarketScanEntry> SelectCandidates(MarketScanResult result, int limit)
    {
        if (limit < 1)
        {
            return [];
        }

        var usable = result.Entries.Where(entry => entry.Usable).ToArray();
        var selected = new List<MarketScanEntry>();
        var bitcoin = usable.FirstOrDefault(entry => entry.Symbol == "BTC_THB");
        if (bitcoin is not null)
        {
            selected.Add(bitcoin);
        }

        foreach (var entry in usable.OrderByDescending(item => item.Volume24h).ThenBy(item => item.Symbol))
        {
            if (selected.Count >= limit / 2)
            {
                break;
            }

            if (!selected.Contains(entry))
            {
                selected.Add(entry);
            }
        }

        var liquidCount = selected.Count;
        foreach (
            var entry in usable
                .OrderByDescending(item => Math.Abs(item.Change24h ?? 0))
                .ThenByDescending(item => item.Range24h)
                .ThenBy(item => item.Symbol)
        )
        {
            if (selected.Count >= limit || selected.Contains(entry))
            {
                continue;
            }

            selected.Add(entry);
        }

        return selected
            .Select(
                (entry, index) => new { entry, rank = index < liquidCount ? index * 2 : (index - liquidCount) * 2 + 1 }
            )
            .OrderBy(item => item.rank)
            .Select(item => item.entry)
            .Take(limit)
            .ToArray();
    }

    internal static bool IsEligibleRule(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("base_asset", out var asset)
        && asset.ValueKind == JsonValueKind.String
        && Text(item, "quote_asset") == "THB"
        && Text(item, "status") == "active"
        && Text(item, "market_segment") == "SPOT"
        && Text(item, "source") == "exchange"
        && Text(item, "base_asset").Length is > 0 and <= 30
        && Text(item, "base_asset").All(char.IsAsciiLetterOrDigit);

    private static bool EquivalentQuotes(JsonElement[] quotes)
    {
        var first = quotes[0];
        return quotes
            .Skip(1)
            .All(quote =>
                EquivalentQuoteValue(quote, first, "last")
                && EquivalentQuoteValue(quote, first, "highest_bid")
                && EquivalentQuoteValue(quote, first, "lowest_ask")
                && EquivalentQuoteValue(quote, first, "quote_volume")
                && EquivalentQuoteValue(quote, first, "percent_change")
                && EquivalentQuoteValue(quote, first, "high_24_hr")
                && EquivalentQuoteValue(quote, first, "low_24_hr")
            );
    }

    private static bool EquivalentQuoteValue(JsonElement quote, JsonElement other, string property)
    {
        var value = OptionalNumber(quote, property);
        var otherValue = OptionalNumber(other, property);
        return value is { } number && otherValue is { } otherNumber
            ? number == otherNumber
            : Text(quote, property) == Text(other, property);
    }

    internal static string Text(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) ? value.ToString() : "";

    private static decimal? OptionalNumber(JsonElement item, string property) =>
        decimal.TryParse(Text(item, property), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
