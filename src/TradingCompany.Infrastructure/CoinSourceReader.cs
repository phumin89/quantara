using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TradingCompany.Application;

namespace TradingCompany.Infrastructure;

// Fixed public catalog: neither a model nor a retrieved document can choose a URL.
public sealed partial class CoinSourceReader(HttpClient client, string directory, TimeProvider clock,
    Func<byte[], CancellationToken, Task<string>>? pdfReader = null)
{
    public static IReadOnlyList<(string Symbol, string Title, string Url)> Catalog { get; } = Array.AsReadOnly<(string Symbol, string Title, string Url)>([
        ("BTC-THB", "Bitcoin whitepaper (2008)", "https://bitcoin.org/bitcoin.pdf"),
        ("BTC-THB", "Bitcoin Core security policy", "https://raw.githubusercontent.com/bitcoin/bitcoin/master/SECURITY.md"),
        ("ETH-THB", "Ethereum original whitepaper (2014)", "https://ethereum.org/whitepaper/"),
        ("ETH-THB", "Ethereum current proof-of-stake documentation", "https://ethereum.org/developers/docs/consensus-mechanisms/pos/"),
        ("SOL-THB", "Solana original whitepaper", "https://solana.com/solana-whitepaper.pdf"),
        ("SOL-THB", "Solana current core concepts", "https://solana.com/docs/core")
    ]);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int ByteLimit = 4 * 1024 * 1024;
    private const int TextLimit = 60_000;
    private sealed record Cached(CoinSourceDocument Document, string RawHash);

    public async Task<CoinSourceDocument> ReadAsync(string url, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Public research directory must be absolute.");
        var spec = Catalog.SingleOrDefault(s => s.Url == url);
        if (spec.Url is null) throw new InvalidDataException("Source is outside the public catalog.");
        Directory.CreateDirectory(directory);
        var index = Path.Combine(directory, Sha(url) + ".json");
        if (File.Exists(index))
        {
            if (new FileInfo(index).Length > ByteLimit) throw new InvalidDataException("Invalid source cache.");
            var saved = JsonSerializer.Deserialize<Cached>(await File.ReadAllBytesAsync(index, token), Json) ?? throw new InvalidDataException("Invalid source cache.");
            var d = saved.Document;
            if (d.Source.Url != url || d.Source.Symbol != spec.Symbol || d.Source.Title != spec.Title ||
                d.Text.Length is < 100 or > TextLimit || d.Source.ContentHash != Sha(url + "\n" + d.Text) ||
                d.Source.Id != "doc-" + d.Source.ContentHash || !CoinResearchValidation.Hash(saved.RawHash) || d.Source.RetrievedAt > clock.GetUtcNow())
                throw new InvalidDataException("Invalid source cache.");
            var rawPath = Path.Combine(directory, saved.RawHash + ".bin");
            if (!File.Exists(rawPath) || new FileInfo(rawPath).Length > ByteLimit ||
                Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(rawPath, token))) != saved.RawHash)
                throw new InvalidDataException("Source snapshot checksum failed.");
            if (clock.GetUtcNow() - d.Source.RetrievedAt < TimeSpan.FromHours(24)) return d;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("QuantaraResearch/1.0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        // The caller installs a no-redirect, no-cookie handler. Unexpected redirection is not source evidence.
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != url || response.Content.Headers.ContentLength > ByteLimit)
            throw new InvalidDataException("Unexpected public source response.");
        var bytes = await ReadBoundedAsync(response, ByteLimit, token);
        string text;
        if (url.EndsWith(".pdf", StringComparison.Ordinal))
        {
            if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new InvalidDataException("Invalid PDF source.");
            text = await (pdfReader ?? ReadPdfAsync)(bytes, token);
        }
        else
        {
            text = Encoding.UTF8.GetString(bytes);
            if (url.Contains("ethereum.org", StringComparison.Ordinal) || url.Contains("solana.com", StringComparison.Ordinal))
            {
                var main = Main().Match(text);
                if (!main.Success) throw new InvalidDataException("Public document has no main content.");
                text = ScriptStyle().Replace(main.Groups[1].Value, " ");
                text = WebUtility.HtmlDecode(Tags().Replace(text, " "));
            }
        }
        text = Normalize(text);
        if (text.Length < 100) throw new InvalidDataException("Source contains too little readable evidence.");
        var truncated = text.Length > TextLimit;
        if (truncated) text = text[..TextLimit];
        var hash = Sha(url + "\n" + text);
        var document = new CoinSourceDocument(new("doc-" + hash, spec.Symbol, spec.Title, url, hash, clock.GetUtcNow()), text, truncated);
        var rawHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var original = Path.Combine(directory, rawHash + ".bin");
        if (!File.Exists(original)) await WriteAtomicAsync(original, bytes, token);
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(new Cached(document, rawHash), Json);
        // Preserve the extracted text and its original-byte mapping for every cited version.
        // The URL index can refresh; saved meeting source IDs still resolve to this snapshot.
        var version = Path.Combine(directory, document.Source.Id + ".json");
        if (!File.Exists(version)) await WriteAtomicAsync(version, cachedBytes, token);
        await WriteAtomicAsync(index, cachedBytes, token);
        return document;
    }

    public static string Normalize(string value) => Whitespace().Replace(value, " ").Trim();
    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Public response exceeds its bound.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken token)
    {
        var pending = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await output.WriteAsync(bytes, token); await output.FlushAsync(token); output.Flush(true); }
            File.Move(pending, path, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
    public static async Task<string> ReadPdfAsync(byte[] bytes, CancellationToken token)
    {
        var folder = Directory.CreateTempSubdirectory("quantara-public-pdf-").FullName;
        var file = Path.Combine(folder, "source.pdf");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var process = new Process { StartInfo = new("pdftotext") { RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.Environment.Clear();
        if (Environment.GetEnvironmentVariable("PATH") is { } path) process.StartInfo.Environment["PATH"] = path;
        foreach (var arg in new[] { "-enc", "UTF-8", "-nopgbrk", file, "-" }) process.StartInfo.ArgumentList.Add(arg);
        try
        {
            await File.WriteAllBytesAsync(file, bytes, deadline.Token); process.Start();
            var stdout = BoundedTextAsync(process.StandardOutput, 240_000, deadline.Token);
            var stderr = BoundedTextAsync(process.StandardError, 8192, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout; _ = await stderr;
            if (process.ExitCode != 0) throw new InvalidDataException("PDF text extraction failed.");
            return output;
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            if (File.Exists(file)) File.Delete(file); Directory.Delete(folder);
        }
    }
    private static async Task<string> BoundedTextAsync(StreamReader input, int maximum, CancellationToken token)
    {
        var output = new StringBuilder(); var buffer = new char[8192]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        { if (output.Length + count > maximum) throw new InvalidDataException("PDF text exceeds its bound."); output.Append(buffer, 0, count); }
        return output.ToString();
    }
    [GeneratedRegex("<main\\b[^>]*>(.*?)</main>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 2000)] private static partial Regex Main();
    [GeneratedRegex("<(script|style)\\b[^>]*>.*?</\\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 2000)] private static partial Regex ScriptStyle();
    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline, 2000)] private static partial Regex Tags();
    [GeneratedRegex("\\s+", RegexOptions.None, 2000)] private static partial Regex Whitespace();
}
