using System.IO;
using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record CoinMarketObservation(string Symbol, bool Active, bool Frozen, decimal Bid, decimal Ask,
    decimal QuoteVolume24h, decimal Change24hPercent);

public static class CoinMarketScout
{
    public static IReadOnlyList<CoinScoutCandidate> Rank(IReadOnlyList<CoinMarketObservation> observations,
        IReadOnlyList<string> executionSymbols, IReadOnlyList<string> heldSymbols, decimal orderNotional, int batchIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(executionSymbols);
        ArgumentNullException.ThrowIfNull(heldSymbols);
        if (orderNotional <= 0 || orderNotional > decimal.MaxValue / 100m)
            throw new ArgumentOutOfRangeException(nameof(orderNotional));
        if (batchIndex < 0) throw new ArgumentOutOfRangeException(nameof(batchIndex));

        var bySymbol = new Dictionary<string, CoinMarketObservation>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            if (!LiveMarketSymbol.IsValid(observation.Symbol))
                throw new InvalidDataException($"Invalid market symbol: {observation.Symbol}");
            if (!bySymbol.TryAdd(observation.Symbol, observation))
                throw new InvalidDataException($"Duplicate market symbol: {observation.Symbol}");
        }

        var execution = ValidSymbols(executionSymbols, nameof(executionSymbols));
        var held = ValidSymbols(heldSymbols, nameof(heldSymbols));
        var legacy = execution.Count <= 3;
        var threshold = Math.Max(100000m, orderNotional * 100m);
        var ranked = bySymbol.Values
            .Where(o => o.Active && !o.Frozen && o.Bid > 0 && o.Ask >= o.Bid && o.QuoteVolume24h >= threshold && o.Change24hPercent >= -100m && o.Change24hPercent <= 1_000_000m)
            .Select(o => TryCandidate(o, execution.Contains(o.Symbol), held.Contains(o.Symbol)))
            .Where(c => c is not null && c.SpreadPercent <= 0.5m).Cast<CoinScoutCandidate>()
            .OrderByDescending(c => c.QuoteVolume24h).ThenBy(c => c.SpreadPercent).ThenByDescending(c => Math.Abs(c.Change24hPercent))
            .ThenBy(c => c.Symbol, StringComparer.Ordinal).ToArray();
        if (!legacy && held.Count >= 8)
        {
            var opportunity = ranked.FirstOrDefault(c => !c.Held);
            var heldCandidates = held.Order(StringComparer.Ordinal).Where(bySymbol.ContainsKey)
                .Select(s => TryCandidate(bySymbol[s], execution.Contains(s), true))
                .Where(c => c is not null).Cast<CoinScoutCandidate>().ToArray();
            var heldBudget = opportunity is null ? 8 : 7;
            var offset = heldCandidates.Length == 0 ? 0 : (int)((long)batchIndex * heldBudget % heldCandidates.Length);
            var batch = heldCandidates.Skip(offset).Concat(heldCandidates.Take(offset)).Take(heldBudget);
            return batch.Concat(opportunity is null ? [] : new[] { opportunity }).ToArray();
        }
        var mandatoryUniverse = (legacy ? execution.Concat(held) : held).Distinct(StringComparer.Ordinal).ToArray();
        var mandatorySymbols = mandatoryUniverse.OrderBy(s => mandatoryUniverse.Length > 8 && held.Contains(s) ? 0 : 1)
            .ThenBy(s => s, StringComparer.Ordinal).Take(8).ToArray();
        var mandatory = mandatorySymbols.Where(bySymbol.ContainsKey)
            .Select(s => TryCandidate(bySymbol[s], execution.Contains(s), held.Contains(s)))
            .Where(c => c is not null).Cast<CoinScoutCandidate>().ToList();

        var opportunities = ranked
            .Where(c => !mandatory.Any(m => m.Symbol == c.Symbol))
            .Take(legacy ? Math.Min(5, 8 - mandatory.Count) : 8 - mandatory.Count);

        return mandatory.Concat(opportunities).Take(8).ToArray();
    }

    private static HashSet<string> ValidSymbols(IEnumerable<string> symbols, string parameterName)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (!LiveMarketSymbol.IsValid(symbol))
                throw new InvalidDataException($"Invalid {parameterName} symbol: {symbol}");
            result.Add(symbol);
            if (result.Count > LiveMarketSymbol.MaximumPayloadMarkets)
                throw new InvalidDataException($"Too many {parameterName} markets.");
        }
        return result;
    }

    private static CoinScoutCandidate? TryCandidate(CoinMarketObservation observation, bool executionAllowed, bool held)
    {
        if (observation.Bid <= 0 || observation.Ask < observation.Bid || observation.QuoteVolume24h < 0 ||
            observation.Change24hPercent < -100m || observation.Change24hPercent > 1_000_000m)
            return null;
        try
        {
            var spread = (observation.Ask - observation.Bid) / observation.Bid * 100m;
            if (spread < 0) return null;
            return new CoinScoutCandidate(observation.Symbol, observation.Bid, observation.Ask, observation.QuoteVolume24h,
                observation.Change24hPercent, spread, 0m, executionAllowed, held);
        }
        catch (OverflowException) { return null; }
    }
}
