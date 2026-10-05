namespace TradingCompany.Domain;

public static class MarketExposurePolicy
{
    private static readonly IReadOnlyDictionary<string, string> EquivalentCoreExposures =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SPY"] = "S&P-500",
            ["VOO"] = "S&P-500",
            ["QQQ"] = "NASDAQ-100",
            ["QQQM"] = "NASDAQ-100"
        };

    private static readonly IReadOnlySet<string> IncomeOverlays =
        new HashSet<string>(["QQQI"], StringComparer.OrdinalIgnoreCase);

    public static bool IsEligiblePassiveCore(Symbol symbol)
        => !IncomeOverlays.Contains(symbol.Value);

    public static bool IsIncomeOverlay(Symbol symbol)
        => IncomeOverlays.Contains(symbol.Value);

    public static bool HasEquivalentIndexExposure(Symbol candidate, IEnumerable<Symbol> currentSymbols)
    {
        ArgumentNullException.ThrowIfNull(currentSymbols);
        if (!EquivalentCoreExposures.TryGetValue(candidate.Value, out var candidateExposure)) return false;
        return currentSymbols.Any(current =>
            current != candidate &&
            EquivalentCoreExposures.TryGetValue(current.Value, out var currentExposure) &&
            string.Equals(candidateExposure, currentExposure, StringComparison.Ordinal));
    }

    public static string CoreExposureKey(Symbol symbol)
        => EquivalentCoreExposures.TryGetValue(symbol.Value, out var exposure)
            ? exposure
            : $"INSTRUMENT:{symbol.Value}";
}
