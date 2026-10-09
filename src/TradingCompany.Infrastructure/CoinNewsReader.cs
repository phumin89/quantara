using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TradingCompany.Application;

namespace TradingCompany.Infrastructure;

public sealed record CoinNewsFeed(string Source, string Url);

public sealed partial class CoinNewsReader(HttpClient client, string directory, TimeProvider clock)
{
    public static IReadOnlyList<CoinNewsFeed> Feeds { get; } = Array.AsReadOnly(new CoinNewsFeed[]
    {
        new("CoinDesk", "https://www.coindesk.com/arc/outboundfeeds/rss"),
        new("Ethereum", "https://blog.ethereum.org/en/feed.xml"),
        new("Federal Reserve", "https://www.federalreserve.gov/feeds/press_monetary.xml")
    });
    private readonly SemaphoreSlim gate = new(1, 1);
    private CoinNewsBrief? cached;
    private string? cachedScope;

    public async Task<CoinNewsBrief> ReadAsync(IReadOnlyCollection<string> universe, IReadOnlyCollection<string> preferred, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(directory) || universe.Count > 1000 || universe.Any(s => !CoinResearchValidation.ValidSymbol(s)) ||
            preferred.Count > 1000 || preferred.Any(s => !CoinResearchValidation.ValidSymbol(s))) throw new InvalidDataException("Invalid news scope.");
        await gate.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow();
            var scope = JsonSerializer.Serialize(new { universe = universe.Distinct().Order(), preferred = preferred.Distinct().Order() });
            if (cached is not null && cachedScope == scope && now >= cached.ObservedAt && now - cached.ObservedAt < TimeSpan.FromMinutes(15)) return cached;
            Directory.CreateDirectory(directory);
            var results = await Task.WhenAll(Feeds.Select(feed => ReadFeedAsync(feed, universe, token)));
            var coverage = results.Select(r => r.Coverage).Append(new CoinNewsCoverage("Bitkub", "UNAVAILABLE")).ToArray();
            // Publisher diversity first, then current relevant headlines. This is routing, not a claim of price impact.
            var ordered = results.SelectMany(r => r.Items).OrderByDescending(i => i.Symbols.Intersect(preferred).Any())
                .ThenByDescending(i => i.SourceAt).ToArray();
            var selected = ordered.GroupBy(i => i.Source).SelectMany(g => g.Take(2)).ToList();
            selected.AddRange(ordered.Where(i => !selected.Any(s => s.Id == i.Id)).Take(12 - selected.Count));
            var items = selected.DistinctBy(i => i.Url).OrderByDescending(i => i.SourceAt).Take(12).ToArray();
            var brief = new CoinNewsBrief(clock.GetUtcNow(), CoinNewsValidation.ChangeKey(items, coverage), items, coverage);
            while (JsonSerializer.SerializeToUtf8Bytes(brief).Length > 12 * 1024 && items.Length > 0)
            {
                items = items[..^1];
                brief = brief with { Items = items, ChangeKey = CoinNewsValidation.ChangeKey(items, coverage) };
            }
            if (!CoinNewsValidation.IsValid(brief, clock.GetUtcNow())) throw new InvalidDataException("Invalid news evidence.");
            await SaveAsync("brief-" + brief.ChangeKey + ".json", JsonSerializer.SerializeToUtf8Bytes(brief), token);
            cached = brief;
            cachedScope = scope;
            return cached;
        }
        finally { gate.Release(); }
    }

    private async Task<(CoinNewsCoverage Coverage, IReadOnlyList<CoinNewsItem> Items)> ReadFeedAsync(
        CoinNewsFeed feed, IReadOnlyCollection<string> universe, CancellationToken token)
    {
        try
        {
            using var response = await client.GetAsync(feed.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.AbsoluteUri != feed.Url || response.Content.Headers.ContentLength > 2 * 1024 * 1024)
                throw new InvalidDataException("Unexpected news response.");
            var bytes = await CoinSourceReader.ReadBoundedAsync(response, 2 * 1024 * 1024, token);
            var retrieved = clock.GetUtcNow();
            var items = Parse(feed, bytes, universe, retrieved);
            var rawHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await SaveAsync("raw-" + rawHash + ".xml", bytes, token);
            await SaveAsync("fetch-" + feed.Source.Replace(' ', '-') + "-" + rawHash + ".json",
                JsonSerializer.SerializeToUtf8Bytes(new { feed.Source, feed.Url, rawHash, retrievedAt = retrieved }), token);
            return (new(feed.Source, "CURRENT"), items);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return (new(feed.Source, "UNAVAILABLE"), []); }
    }

    public static IReadOnlyList<CoinNewsItem> Parse(CoinNewsFeed feed, byte[] bytes, IReadOnlyCollection<string> universe, DateTimeOffset retrieved)
    {
        if (!Feeds.Contains(feed) || bytes.Length > 2 * 1024 * 1024 || universe.Count > 1000 ||
            universe.Any(s => !CoinResearchValidation.ValidSymbol(s))) throw new InvalidDataException("Invalid news source.");
        using var input = new MemoryStream(bytes);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 2 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        if (!(document.Root?.Name.LocalName is "rss" or "feed")) throw new InvalidDataException("Invalid news feed.");
        var container = document.Root.Name.LocalName == "rss" ? document.Root.Elements().SingleOrDefault(e => e.Name.LocalName == "channel") : document.Root;
        if (container is null) throw new InvalidDataException("News channel is missing.");
        var entries = container.Elements().Where(e => e.Name.LocalName is "item" or "entry").Take(1001).ToArray();
        if (entries.Length > 1000) throw new InvalidDataException("Invalid news feed.");
        var items = new List<CoinNewsItem>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var title = Clean(Value("title"), 180);
            var link = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link" &&
                (e.Attribute("rel") is null || e.Attribute("rel")!.Value == "alternate"));
            var url = (link?.Attribute("href")?.Value ?? link?.Value)?.Trim();
            var published = Value("pubDate") ?? Value("published"); var dateKind = published is null ? "UPDATED" : "PUBLISHED";
            var date = published ?? Value("updated");
            if (title.Length == 0 || !CoinNewsValidation.PublicUrl(feed.Source, url) || date is null || !ExplicitZone().IsMatch(date) ||
                !DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sourceAt))
                throw new InvalidDataException("News provenance is incomplete.");
            sourceAt = sourceAt.ToUniversalTime();
            if (sourceAt > retrieved || retrieved - sourceAt > TimeSpan.FromDays(7)) continue;
            if (!seen.Add(url!)) throw new InvalidDataException("Duplicate news URL.");
            var text = Clean(Value("description") ?? Value("summary") ?? title, 280);
            if (text.Length == 0) text = title;
            var symbols = MatchSymbols(feed.Source, title + " " + text, universe).Take(8).ToArray();
            var marketWide = feed.Source == "Federal Reserve" || MarketContext().IsMatch(title + " " + text);
            if (symbols.Length == 0 && !marketWide) continue;
            var hash = CoinNewsValidation.ContentHash(feed.Source, title, url!, sourceAt, dateKind, text);
            items.Add(new("news-" + hash, feed.Source, CoinNewsValidation.Kind(feed.Source)!, title, url!, sourceAt,
                dateKind, retrieved, hash, symbols, marketWide, text));

            string? Value(string name) => entry.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        }
        return items;
    }

    private static IEnumerable<string> MatchSymbols(string source, string text, IEnumerable<string> universe)
    {
        var tickers = Tickers().Matches(text).Select(m => m.Value).Where(t => t.Any(char.IsAsciiLetter)).ToHashSet(StringComparer.Ordinal);
        foreach (Match match in MarkedTickers().Matches(text)) tickers.Add(match.Groups["ticker"].Value);
        foreach (var symbol in universe.Distinct().Order())
        {
            var ticker = symbol[..^4];
            var name = ticker switch { "BTC" => "bitcoin", "ETH" => "ethereum|ether", "SOL" => "solana", "XRP" => "ripple",
                "DOGE" => "dogecoin", "ADA" => "cardano", "USDT" => "tether", "DOT" => "polkadot", "CRV" => "curve finance", _ => null };
            // Single-letter and numeric names need an explicit ticker marker; U.S. is not the S token.
            if (tickers.Contains(ticker) || (source == "Ethereum" && ticker == "ETH") || (name is not null &&
                Regex.IsMatch(text, @"\b(?:" + name + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))) yield return symbol;
        }
    }

    private static string Clean(string? html, int maximum)
    {
        var text = CoinSourceReader.Normalize(Tags().Replace(Scripts().Replace(WebUtility.HtmlDecode(html ?? ""), " "), " "));
        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        return text.Length <= maximum ? text : text[..(maximum - 1)] + "…";
    }

    private async Task SaveAsync(string name, byte[] bytes, CancellationToken token)
    {
        var path = Path.Combine(directory, name);
        if (File.Exists(path)) return;
        var pending = path + ".pending-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(pending, bytes, token); File.Move(pending, path, overwrite: true); }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9.])[A-Z0-9]{2,15}(?![A-Za-z0-9.])", RegexOptions.CultureInvariant)] private static partial Regex Tickers();
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:\$(?<ticker>[A-Z0-9]{1,15})\b|\((?<ticker>[A-Z0-9]{1,15})\)|(?<ticker>[A-Z0-9]{1,15})-THB\b)", RegexOptions.CultureInvariant)] private static partial Regex MarkedTickers();
    [GeneratedRegex(@"(Z|GMT|[+-]\d{2}:?\d{2})\s*$", RegexOptions.CultureInvariant)] private static partial Regex ExplicitZone();
    [GeneratedRegex(@"\b(crypto|cryptocurrency|digital assets?|interest rates?|monetary|FOMC)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex MarketContext();
    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)] private static partial Regex Scripts();
    [GeneratedRegex(@"<[^>]*>", RegexOptions.Singleline, 1000)] private static partial Regex Tags();
}
