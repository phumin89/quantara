using TradingCompany.Application;
using System.Security.Cryptography;
using System.Text.Json;

namespace TradingCompany.Infrastructure;

public sealed class JsonForwardPaperStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Envelope(string Hash, ForwardPaperAccount Account);
    private readonly string path;
    public JsonForwardPaperStore(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Paper journal path must be absolute.");
        this.path = Path.GetFullPath(path);
    }

    public ForwardPaperAccount? Read()
    {
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".lock")) throw new InvalidDataException("Initialized paper journal is missing.");
            return null;
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var saved = JsonSerializer.Deserialize<Envelope>(stream, Json);
            if (saved?.Account is null || saved.Hash != Hash(saved.Account)) throw new InvalidDataException("Paper journal checksum mismatch.");
            ForwardPaperLedger.Validate(saved.Account);
            return saved.Account;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Paper journal cannot be reconciled.", ex); }
    }

    public ForwardPaperAccount Update(Func<ForwardPaperAccount?, ForwardPaperAccount> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var lease = Acquire(out var created);
        var before = File.Exists(path) ? Read() : created ? null : throw new InvalidDataException("Missing paper journal; no automatic reset.");
        // Capture the prefix before the callback, including if a caller attempts to mutate an array in place.
        var identity = before?.FundingAccountId;
        var prefix = before?.Events.Select(e => JsonSerializer.Serialize(e, Json)).ToArray() ?? [];
        var next = change(before);
        ForwardPaperLedger.Validate(next);
        if (identity is not null && (next.FundingAccountId != identity || next.Events.Count < prefix.Length ||
            !next.Events.Take(prefix.Length).Select(e => JsonSerializer.Serialize(e, Json)).SequenceEqual(prefix)))
            throw new InvalidDataException("Paper journal must preserve identity and append-only history.");
        if (before is not null && next.Events.Count == prefix.Length) return next;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Envelope(Hash(next), next), Json);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return next;
    }

    private FileStream Acquire(out bool created)
    {
        try
        {
            var lease = new FileStream(path + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true; return lease;
        }
        catch (IOException) when (File.Exists(path + ".lock")) { }
        created = false;
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            try { return new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < deadline) { Thread.Sleep(20); }
        }
    }

    private static string Hash(ForwardPaperAccount account) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(account, Json)));
}
