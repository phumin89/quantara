using System.Security.Cryptography;
using System.Text;

namespace TradingCompany.Infrastructure;

// A wallet observation is not an execution account, reconciliation or trading permission.
public sealed record BitkubWalletObservation(DateTimeOffset ObservedAt, string CredentialReference,
    IReadOnlyList<BrokerWalletBalance> Balances)
{
    public string Source => "Bitkub";
    public bool ReadAccessVerified => true;
    public bool TradingEnabled => false;
    public bool AccountIdentityVerified => false;
}

public sealed class BitkubWalletReader : IDisposable
{
    private readonly string credentialsPath;
    private readonly TimeProvider clock;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<BrokerWalletBalance>>> readWallet;
    private readonly SemaphoreSlim gate = new(1);
    private BitkubWalletObservation? cached;

    public BitkubWalletReader(string credentialsPath, TimeProvider clock,
        Func<string, string, CancellationToken, Task<IReadOnlyList<BrokerWalletBalance>>>? readWallet = null)
    {
        if (!Path.IsPathFullyQualified(credentialsPath)) throw new ArgumentException("Absolute credentials path required.");
        this.credentialsPath = credentialsPath;
        this.clock = clock;
        this.readWallet = readWallet ?? ReadFromBroker;
    }

    public async Task<BitkubWalletObservation> ReadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var (key, secret) = BitkubCredentialsFile.Read(credentialsPath);
            var reference = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "\0" + secret)));
            var now = clock.GetUtcNow();
            if (cached is not null && cached.CredentialReference == reference && now >= cached.ObservedAt &&
                now - cached.ObservedAt < TimeSpan.FromSeconds(20)) return cached;
            cached = null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var balances = await readWallet(key, secret, deadline.Token);
            if (balances.Count is 0 or > 256 || !balances.Any(b => b.Currency == "THB") ||
                balances.Select(b => b.Currency).Distinct().Count() != balances.Count ||
                balances.Any(b => string.IsNullOrEmpty(b.Currency) || b.Currency.Length > 20 ||
                    b.Currency.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c)) ||
                    b.Available < 0 || b.Reserved < 0 || b.Total < 0 || b.Available + b.Reserved != b.Total))
                throw new InvalidDataException("Wallet evidence unavailable.");
            cached = new(clock.GetUtcNow(), reference, balances.Where(b => b.Currency == "THB" || b.Total != 0).ToArray());
            return cached;
        }
        catch { cached = null; throw; }
        finally { gate.Release(); }
    }

    private static async Task<IReadOnlyList<BrokerWalletBalance>> ReadFromBroker(string key, string secret, CancellationToken token)
    {
        using var broker = new BitkubLiveBroker(key, secret);
        return await broker.GetWalletAsync(token);
    }
    public void Dispose() => gate.Dispose();
}
