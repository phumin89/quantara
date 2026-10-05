using System.Security.Cryptography;
using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.Infrastructure;

/// Isolated cash-only treasury. No portfolio, broker or backtest dependency.
public sealed class JsonForwardFundingStore : IForwardFundingStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string path;
    private sealed record Envelope(string Hash, ForwardFundingAccount Account);

    public JsonForwardFundingStore(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Account path must be absolute.", nameof(path));
        this.path = Path.GetFullPath(path);
    }

    public ForwardFundingAccount? Read()
    {
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".lock")) throw new InvalidDataException("Forward account missing; refusing to reset.");
            return null;
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var saved = JsonSerializer.Deserialize<Envelope>(stream, Json) ?? throw new InvalidDataException("Empty account.");
            if (saved.Account is null || saved.Hash != Hash(saved.Account)) throw new InvalidDataException("Forward account integrity failed.");
            ForwardFundingRules.Validate(saved.Account);
            return saved.Account;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Forward account is invalid; refusing to reset.", ex); }
    }

    public ForwardFundingAccount Advance(DateTimeOffset now)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var lease = AcquireLock(out var created);
        var current = File.Exists(path) ? Read()! : !created
            ? throw new InvalidDataException("Forward account missing; refusing to reset.") : ForwardFundingRules.Open(now);
        if (now < current.OpenedAt || current.Postings.Any(p => p.PostedAt > now))
            throw new InvalidOperationException("Clock is before recorded account activity.");
        var rows = current.Postings.ToList();
        var due = current.NextPayday;
        var today = ForwardFundingCalendar.BangkokDate(now);
        var reserve = current.EmergencyBalance;
        while (due <= today)
        {
            var row = ForwardFundingRules.Allocate(current.Mandate, due, reserve);
            rows.Add(new(now, row));
            reserve = row.EmergencyBalance;
            due = ForwardFundingCalendar.MonthEnd(due.AddDays(1));
        }
        if (File.Exists(path) && rows.Count == current.Postings.Count) return current;
        var next = current with { Postings = rows.ToArray() };
        ForwardFundingRules.Validate(next);
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

    private FileStream AcquireLock(out bool created)
    {
        try
        {
            var first = new FileStream(path + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true;
            return first;
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

    private static string Hash(ForwardFundingAccount account)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(account, Json)));
}
