namespace TradingCompany.Domain;

// A payload bound, not a business limit or an execution allowlist.
public static class LiveMarketSymbol
{
    public const int MaximumPayloadMarkets = 1000;

    public static bool IsValid(string? symbol) => symbol is { Length: >= 5 and <= 19 } &&
        symbol.EndsWith("-THB", StringComparison.Ordinal) && symbol[..^4] != "THB" &&
        symbol[..^4].All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c));
}
