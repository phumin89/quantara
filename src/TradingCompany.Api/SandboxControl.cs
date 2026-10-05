using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace TradingCompany.Api;

// Device-local Sandbox control only. This token confers no Live authority.
public sealed class SandboxControl(string path, string webOrigin, IPAddress? hostProxyAddress = null)
{
    private sealed record State(int Version, bool? Paused);
    private readonly object gate = new();
    private readonly byte[] token = RandomNumberGenerator.GetBytes(32);
    public string Token => Convert.ToHexString(token);

    public bool Paused
    {
        get
        {
            lock (gate)
            {
                if (!File.Exists(path))
                {
                    if (File.Exists(path + ".lock")) throw new InvalidDataException("Sandbox control state missing.");
                    return false;
                }
                var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
                if (state is not { Version: 1, Paused: not null }) throw new InvalidDataException("Invalid Sandbox control state.");
                return state.Paused.Value;
            }
        }
    }

    public void SetPaused(bool paused)
    {
        lock (gate)
        {
            _ = Paused; // Never replace missing or malformed saved control state.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, new State(1, paused));
                    stream.Flush(true);
                }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public bool Trusted(HttpContext context, bool mutation = false)
    {
        var address = context.Connection.RemoteIpAddress;
        if (!Uri.TryCreate(webOrigin, UriKind.Absolute, out var origin) || !origin.IsLoopback || origin.Scheme is not ("http" or "https") ||
            address is null || !TrustedAddress(address) ||
            context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "::1") ||
            context.Request.Headers.Origin.ToString() != webOrigin) return false;
        if (!mutation) return true;
        var supplied = context.Request.Headers["X-Sandbox-Control"].ToString();
        if (supplied.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(supplied), token); }
        catch (FormatException) { return false; }
    }

    private bool TrustedAddress(IPAddress address)
    {
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        // The Docker launcher publishes this paper-only API on host loopback and supplies
        // its exact bridge gateway. Other containers and remote addresses remain untrusted.
        return IPAddress.IsLoopback(normalized) || hostProxyAddress is not null && normalized.Equals(hostProxyAddress);
    }
}

public sealed record SandboxPauseRequest(bool? Paused);
