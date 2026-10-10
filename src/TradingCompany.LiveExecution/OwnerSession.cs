using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace TradingCompany.LiveExecution;

/// <summary>Remembered control requires explicit consent during local owner pairing.</summary>
public sealed class OwnerSession
{
    public static readonly TimeSpan ViewingLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan ControlLifetime = TimeSpan.FromHours(8);
    private readonly TimeProvider clock;
    private readonly IDataProtector? protector;
    private readonly string? sessionFile;
    private readonly object sync = new();
    private byte[]? pairingHash;
    private DateTimeOffset pairingExpires;
    private DateTimeOffset attemptWindow;
    private int attempts;
    private byte[]? sessionHash;
    private DateTimeOffset sessionExpires;
    private DateTimeOffset controlExpires;

    public OwnerSession(TimeProvider clock, IDataProtectionProvider? provider = null, string? stateDirectory = null)
    {
        this.clock = clock;
        if (stateDirectory is null) return;
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector("Quantara.RememberedViewing.v1");
        Directory.CreateDirectory(stateDirectory);
        sessionFile = Path.Combine(stateDirectory, "session.bin");
        try
        {
            if (!File.Exists(sessionFile)) return;
            if (new FileInfo(sessionFile).Length > 4096) return;
            var saved = JsonSerializer.Deserialize<SavedViewing>(protector.Unprotect(File.ReadAllBytes(sessionFile)));
            var now = clock.GetUtcNow();
            if (saved is null || saved.Expires <= now || saved.Expires > now.Add(ViewingLifetime) || saved.Hash is not { Length: 64 }) return;
            sessionHash = Convert.FromHexString(saved.Hash);
            sessionExpires = saved.Expires;
            if (saved.ControlExpires is { } control && control > now && control <= saved.Expires)
                controlExpires = control;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException)
        {
            // Invalid/missing protected state grants no access. Never expose its content.
        }
    }

    // Called only by the local interactive operator, never exposed through HTTP.
    public string IssuePairingCode()
    {
        lock (sync)
        {
            SaveViewing(null);
            var code = RandomNumberGenerator.GetBytes(32);
            pairingHash = SHA256.HashData(code);
            pairingExpires = clock.GetUtcNow().AddMinutes(5);
            attemptWindow = clock.GetUtcNow();
            attempts = 0;
            sessionHash = null;
            controlExpires = default;
            var text = Convert.ToHexString(code);
            CryptographicOperations.ZeroMemory(code);
            return text;
        }
    }

    public (int Status, string? SessionId) TryPair(string? code, bool rememberBrowser = false, bool rememberControl = false)
    {
        lock (sync)
        {
            var now = clock.GetUtcNow();
            if (now - attemptWindow >= TimeSpan.FromMinutes(1))
            {
                attemptWindow = now;
                attempts = 0;
            }
            if (++attempts > 5) return (429, null);
            if (rememberControl && !rememberBrowser) return (400, null);
            if (pairingHash is null || now >= pairingExpires || code is null || code.Length != 64)
                return (401, null);
            byte[] bytes;
            try { bytes = Convert.FromHexString(code); }
            catch (FormatException) { return (401, null); }
            var matches = CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), pairingHash);
            CryptographicOperations.ZeroMemory(bytes);
            if (!matches) return (401, null);
            if (rememberBrowser && protector is null) return (503, null);
            var id = Guid.NewGuid().ToString("N");
            var hash = Hash(id);
            var expires = now.Add(rememberBrowser ? ViewingLifetime : ControlLifetime);
            var control = now.Add(rememberControl ? ViewingLifetime : ControlLifetime);
            SaveViewing(rememberBrowser ? new(Convert.ToHexString(hash), expires, rememberControl ? control : null) : null);
            pairingHash = null;
            sessionHash = hash;
            sessionExpires = expires;
            controlExpires = control;
            return (204, id);
        }
    }

    public bool IsActive(string? id)
    {
        lock (sync) return Matches(id) && clock.GetUtcNow() < sessionExpires;
    }

    public bool CanControl(string? id)
    {
        lock (sync) return Matches(id) &&
            clock.GetUtcNow() < controlExpires && clock.GetUtcNow() < sessionExpires;
    }

    public void Revoke(string? id)
    {
        lock (sync)
        {
            if (!Matches(id)) return;
            SaveViewing(null);
            sessionHash = null;
            controlExpires = default;
        }
    }

    private bool Matches(string? id) => id is { Length: 32 } && sessionHash is not null &&
        CryptographicOperations.FixedTimeEquals(Hash(id), sessionHash);

    private static byte[] Hash(string id) => SHA256.HashData(Encoding.ASCII.GetBytes(id));

    private void SaveViewing(SavedViewing? saved)
    {
        if (sessionFile is null || protector is null) return;
        if (saved is null)
        {
            File.Delete(sessionFile);
            return;
        }
        var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(saved));
        var temporary = sessionFile + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, sessionFile, overwrite: true);
    }

    private sealed record SavedViewing(string Hash, DateTimeOffset Expires,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        DateTimeOffset? ControlExpires = null);
}
