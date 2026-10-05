using TradingCompany.Application;
using System.Security.Cryptography;
using System.Text.Json;

namespace TradingCompany.Infrastructure;

public sealed class JsonForwardInvestmentStore : IForwardInvestmentStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Envelope(string Hash, ForwardInvestmentAccount Account);
    private readonly string path;

    public JsonForwardInvestmentStore(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Portfolio path must be absolute.");
        this.path = Path.GetFullPath(path);
    }

    public ForwardInvestmentAccount? Read()
    {
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".lock")) throw new InvalidDataException("Initialized investment portfolio is missing.");
            return null;
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var saved = JsonSerializer.Deserialize<Envelope>(stream, Json);
            if (saved?.Account is null || saved.Hash != Hash(saved.Account)) throw new InvalidDataException("Investment integrity check failed.");
            var account = saved.Account;
            if (account.Version != 1 || account.FundingAccountId == Guid.Empty || account.Credits is null ||
                account.Credits.Any(c => c is null || c.Amount <= 0 || c.SalaryDate == default || c.CreditedAt == default) ||
                account.Credits.Select(c => c.SalaryDate).Distinct().Count() != account.Credits.Count)
                throw new InvalidDataException("Investment ledger is invalid.");
            return account;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Investment ledger cannot be verified.", ex); }
    }

    public ForwardInvestmentAccount Synchronize(ForwardFundingAccount funding, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(funding);
        funding = funding with { Postings = funding.Postings.ToArray() };
        ForwardFundingRules.Validate(funding);
        if (now < funding.OpenedAt || funding.Postings.Any(p => p.PostedAt > now))
            throw new InvalidOperationException("Cannot credit future treasury records.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var lease = Acquire(out var created);
        var current = File.Exists(path) ? Read()! : created
            ? new ForwardInvestmentAccount(1, funding.AccountId, [])
            : throw new InvalidDataException("Investment state missing; no automatic reset.");
        ForwardInvestmentReconciliation.Pending(funding, current);
        if (current.Credits.Any(c => c.CreditedAt > now)) throw new InvalidOperationException("Clock is before recorded investment credits.");
        var additions = funding.Postings.Where(p => p.Allocation.Investment > 0).Skip(current.Credits.Count)
            .Select(p => new ForwardInvestmentCredit(p.Allocation.SalaryDate, p.Allocation.Investment, now)).ToArray();
        if (additions.Length == 0 && File.Exists(path)) return current;
        var next = current with { Credits = current.Credits.Concat(additions).ToArray() };
        if (ForwardInvestmentReconciliation.Pending(funding, next) != 0m)
            throw new InvalidDataException("Investment transfer does not reconcile.");
        Save(next);
        return next;
    }

    private void Save(ForwardInvestmentAccount account)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Envelope(Hash(account), account), Json);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private FileStream Acquire(out bool created)
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

    private static string Hash(ForwardInvestmentAccount account) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(account, Json)));
}
