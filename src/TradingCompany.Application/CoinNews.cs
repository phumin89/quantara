using System.Security.Cryptography;
using System.Text.Json;

namespace TradingCompany.Application;

public sealed record CoinNewsItem(string Id, string Source, string Kind, string Title, string Url,
    DateTimeOffset SourceAt, string DateKind, DateTimeOffset RetrievedAt, string ContentHash,
    IReadOnlyList<string> Symbols, bool MarketWide, string EvidenceText);
public sealed record CoinNewsCoverage(string Source, string Status);
public sealed record CoinNewsBrief(DateTimeOffset ObservedAt, string ChangeKey,
    IReadOnlyList<CoinNewsItem> Items, IReadOnlyList<CoinNewsCoverage> Coverage);

// Headlines and feed excerpts are evidence to challenge, never order authority.
public static class CoinNewsValidation
{
    public static string? Kind(string source) => source switch
    {
        "CoinDesk" => "REPORT", "Ethereum" or "Federal Reserve" or "Bitkub" => "OFFICIAL", _ => null
    };

    public static bool PublicUrl(string source, string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || value!.Length > 400 ||
            value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || value.Contains('#') || (source != "Bitkub" && value.Contains('?'))) return false;
        return source switch
        {
            "CoinDesk" => uri.Host == "www.coindesk.com" && uri.Query.Length == 0 && uri.AbsolutePath.Length > 1,
            "Ethereum" => uri.Host == "blog.ethereum.org" && uri.Query.Length == 0 && uri.AbsolutePath.StartsWith("/en/", StringComparison.Ordinal),
            "Federal Reserve" => uri.Host == "www.federalreserve.gov" && uri.Query.Length == 0 &&
                uri.AbsolutePath.StartsWith("/newsevents/pressreleases/", StringComparison.Ordinal),
            "Bitkub" => uri.Host == "support.bitkub.com" && uri.AbsolutePath == "/en/solutions/articles" &&
                uri.Query.StartsWith("?id=KM", StringComparison.Ordinal) && uri.Query.Length == 15 && uri.Query[6..].All(char.IsAsciiDigit),
            _ => false
        };
    }

    public static string ContentHash(string source, string title, string url, DateTimeOffset sourceAt, string dateKind, string text) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { source, title, url, sourceAt, dateKind, text })));

    public static string ChangeKey(IEnumerable<CoinNewsItem> items, IEnumerable<CoinNewsCoverage> coverage) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            items = items.Select(i => new { i.Id, i.Symbols, i.MarketWide }).OrderBy(i => i.Id),
            coverage = coverage.OrderBy(c => c.Source)
        })));

    public static bool IsValid(CoinNewsBrief? brief, DateTimeOffset now)
    {
        if (brief is null || brief.ObservedAt > now || now - brief.ObservedAt > TimeSpan.FromMinutes(16) ||
            brief.Items is not { Count: <= 12 } || brief.Coverage is not { Count: > 0 and <= 4 } ||
            brief.Coverage.Any(c => c is null || Kind(c.Source) is null || c.Status is not ("CURRENT" or "UNAVAILABLE")) ||
            brief.Coverage.Select(c => c.Source).Distinct().Count() != brief.Coverage.Count ||
            brief.Items.Any(i => i is null || Kind(i.Source) != i.Kind || !PublicUrl(i.Source, i.Url) ||
                !Text(i.Title, 180) || !Text(i.EvidenceText, 280) || i.DateKind is not ("PUBLISHED" or "UPDATED") ||
                i.SourceAt > brief.ObservedAt || brief.ObservedAt - i.SourceAt > TimeSpan.FromDays(7) ||
                i.RetrievedAt > brief.ObservedAt || brief.ObservedAt - i.RetrievedAt > TimeSpan.FromMinutes(16) ||
                i.Symbols is not { Count: <= 8 } || i.Symbols.Any(s => !CoinResearchValidation.ValidSymbol(s)) ||
                i.Symbols.Distinct().Count() != i.Symbols.Count ||
                !brief.Coverage.Any(c => c.Source == i.Source && c.Status == "CURRENT") ||
                i.ContentHash != ContentHash(i.Source, i.Title, i.Url, i.SourceAt, i.DateKind, i.EvidenceText) || i.Id != "news-" + i.ContentHash) ||
            brief.Items.Select(i => i.Id).Distinct().Count() != brief.Items.Count ||
            brief.Items.Select(i => i.Url).Distinct().Count() != brief.Items.Count) return false;
        return brief.ChangeKey == ChangeKey(brief.Items, brief.Coverage) && JsonSerializer.SerializeToUtf8Bytes(brief).Length <= 12 * 1024;
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
}
