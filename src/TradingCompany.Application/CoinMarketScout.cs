using System.IO;

namespace TradingCompany.Application;

public sealed record CoinMarketObservation(string Symbol, bool Active, bool Frozen, decimal Bid, decimal Ask,
    decimal QuoteVolume24h, decimal Change24hPercent);

public static class CoinMarketScout
{
    public static IReadOnlyList<CoinScoutCandidate> Rank(IReadOnlyList<CoinMarketObservation> observations,
        IReadOnlyList<string> executionSymbols, IReadOnlyList<string> heldSymbols, decimal orderNotional)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(executionSymbols);
        ArgumentNullException.ThrowIfNull(heldSymbols);
        if (orderNotional <= 0 || orderNotional > decimal.MaxValue / 100m)
            throw new ArgumentOutOfRangeException(nameof(orderNotional));

        var bySymbol = new Dictionary<string, CoinMarketObservation>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            if (!CoinResearchValidation.ValidSymbol(observation.Symbol) || observation.Symbol != observation.Symbol.ToUpperInvariant())
                throw new InvalidDataException($"Invalid market symbol: {observation.Symbol}");
            if (!bySymbol.TryAdd(observation.Symbol, observation))
                throw new InvalidDataException($"Duplicate market symbol: {observation.Symbol}");
        }

        var execution = ValidSymbols(executionSymbols, nameof(executionSymbols));
        var held = ValidSymbols(heldSymbols, nameof(heldSymbols));
        var mandatorySymbols = execution.Concat(held).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        if (mandatorySymbols.Length > 3) throw new InvalidDataException("At most three mandatory markets are supported.");
        var mandatory = mandatorySymbols.Where(bySymbol.ContainsKey)
            .Select(s => TryCandidate(bySymbol[s], execution.Contains(s), held.Contains(s)))
            .Where(c => c is not null).Cast<CoinScoutCandidate>().ToList();

        var threshold = Math.Max(100000m, orderNotional * 100m);
        var opportunities = bySymbol.Values
            .Where(o => o.Active && !o.Frozen && o.Bid > 0 && o.Ask >= o.Bid && o.QuoteVolume24h >= threshold && o.Change24hPercent >= -100m && o.Change24hPercent <= 1_000_000m)
            .Select(o => TryCandidate(o, execution.Contains(o.Symbol), held.Contains(o.Symbol)))
            .Where(c => c is not null && c.SpreadPercent <= 0.5m).Cast<CoinScoutCandidate>()
            .Where(c => !mandatory.Any(m => m.Symbol == c.Symbol))
            .OrderByDescending(c => c.QuoteVolume24h).ThenByDescending(c => Math.Abs(c.Change24hPercent))
            .ThenBy(c => c.Symbol, StringComparer.Ordinal).Take(5);

        return mandatory.Concat(opportunities).Take(8).ToArray();
    }

    private static HashSet<string> ValidSymbols(IEnumerable<string> symbols, string parameterName)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (!CoinResearchValidation.ValidSymbol(symbol) || symbol != symbol.ToUpperInvariant())
                throw new InvalidDataException($"Invalid {parameterName} symbol: {symbol}");
            result.Add(symbol);
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
