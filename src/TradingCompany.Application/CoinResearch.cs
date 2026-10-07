using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradingCompany.Application;

public sealed record CoinScoutCandidate(string Symbol, decimal Bid, decimal Ask, decimal QuoteVolume24h,
    decimal Change24hPercent, decimal SpreadPercent, decimal DepthQuoteNotional, bool ExecutionAllowed, bool Held)
{
    public string EvidenceId => "scout:" + Symbol;
}
public sealed record CoinResearchSource(string Id, string Symbol, string Title, string Url,
    string ContentHash, DateTimeOffset RetrievedAt);
public sealed record CoinSourceDocument(CoinResearchSource Source, string Text, bool Truncated);
public sealed record CoinResearchClaim(string Statement, string Assessment, string SourceId, string Quote);
public sealed record CoinCredibilityFinding(string Symbol, string Summary, IReadOnlyList<CoinResearchClaim> Claims);
public sealed record CoinResearchBrief(DateTimeOffset ObservedAt, string ChangeKey,
    IReadOnlyList<CoinScoutCandidate> Watchlist, IReadOnlyList<CoinResearchSource> Sources,
    IReadOnlyList<CoinCredibilityFinding> Findings, IReadOnlyList<string> Gaps);

public interface ICoinResearchProvider
{
    Task<CoinResearchBrief> ReadAsync(LiveResearchPacket packet, CancellationToken cancellationToken);
}

// Public provenance only. Research is never execution authority.
public static partial class CoinResearchValidation
{
    public static bool ValidSymbol(string? value) => value is not null && SymbolPattern().IsMatch(value);
    public static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    public static bool PublicSourceUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var u) &&
        u.Scheme == "https" && u.IsDefaultPort && u.UserInfo.Length == 0 && u.Query.Length == 0 && u.Fragment.Length == 0 &&
        u.Host is "bitcoin.org" or "raw.githubusercontent.com" or "ethereum.org" or "solana.com";
    public static bool IsValid(CoinResearchBrief? value, DateTimeOffset now)
    {
        if (value is null || value.ObservedAt > now || now - value.ObservedAt > TimeSpan.FromMinutes(16) || !Hash(value.ChangeKey) ||
            value.Watchlist is not { Count: <= 8 } || value.Sources is not { Count: <= 6 } || value.Findings is not { Count: <= 3 } ||
            value.Gaps is not { Count: <= 12 } || value.Gaps.Any(g => !Text(g, 200)) ||
            value.Watchlist.Any(c => c is null || !ValidSymbol(c.Symbol) || c.Bid <= 0 || c.Ask < c.Bid || c.QuoteVolume24h < 0 ||
                c.Change24hPercent is < -100 or > 1_000_000 || c.SpreadPercent < 0 || c.DepthQuoteNotional < 0) || value.Watchlist.Select(c => c.Symbol).Distinct().Count() != value.Watchlist.Count ||
            value.Sources.Any(s => s is null || !ValidSymbol(s.Symbol) || !Text(s.Title, 150) || !PublicSourceUrl(s.Url) ||
                !Hash(s.ContentHash) || s.Id != "doc-" + s.ContentHash || s.RetrievedAt > now || now - s.RetrievedAt > TimeSpan.FromDays(2)) ||
            value.Sources.Select(s => s.Id).Distinct().Count() != value.Sources.Count ||
            value.Findings.Any(f => f is null || !ValidSymbol(f.Symbol) || !Text(f.Summary, 600) || f.Claims is not { Count: > 0 and <= 3 } ||
                f.Claims.Any(c => c is null || !Text(c.Statement, 400) || !Text(c.Quote, 400) || c.Quote.Length < 20 ||
                    c.Assessment is not ("PROJECT_CLAIM" or "CONTRADICTED" or "UNCERTAIN") ||
                    !value.Sources.Any(s => s.Id == c.SourceId && s.Symbol == f.Symbol))) ||
            value.Findings.Select(f => f.Symbol).Distinct().Count() != value.Findings.Count) return false;
        return JsonSerializer.SerializeToUtf8Bytes(value).Length <= 24 * 1024;
    }
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
    [GeneratedRegex("^[A-Z0-9]{1,15}-THB$")]
    private static partial Regex SymbolPattern();
}
