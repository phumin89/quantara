using System.Net;
using System.Net.Sockets;

namespace Quantara.MarketData.Infrastructure.News;

public static class NewsNetworkPolicy
{
    public static bool IsAllowedEndpoint(string host, int port) =>
        port == 443
        && (
            host.Equals("www.federalreserve.gov", StringComparison.OrdinalIgnoreCase)
            || host.Equals("federalreserve.gov", StringComparison.OrdinalIgnoreCase)
            || host.Equals("www.bls.gov", StringComparison.OrdinalIgnoreCase)
            || host.Equals("bls.gov", StringComparison.OrdinalIgnoreCase)
        );

    public static bool IsAllowedLink(Uri source, Uri candidate) =>
        candidate.Scheme == Uri.UriSchemeHttps
        && candidate.Port == 443
        && string.IsNullOrEmpty(candidate.UserInfo)
        && (
            string.Equals(source.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
            || candidate.Host.EndsWith(".federalreserve.gov", StringComparison.OrdinalIgnoreCase)
            || candidate.Host.Equals("federalreserve.gov", StringComparison.OrdinalIgnoreCase)
            || candidate.Host.EndsWith(".bls.gov", StringComparison.OrdinalIgnoreCase)
            || candidate.Host.Equals("bls.gov", StringComparison.OrdinalIgnoreCase)
        );

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublic(address.MapToIPv4());
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is not (0 or 10 or 127)
                && bytes[0] < 224
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && (bytes[1] is 0 or 168 || bytes[1] == 88 && bytes[2] == 99))
                && !(bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100))
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                && !(bytes[0] == 169 && bytes[1] == 254);
        }

        var ipv6 = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetworkV6
            && (ipv6[0] & 0xe0) == 0x20
            && !(ipv6[0] == 0x20 && ipv6[1] == 1 && ipv6[2] == 0x0d && ipv6[3] == 0xb8)
            && !(ipv6[0] == 0x20 && ipv6[1] == 2)
            && !(ipv6[0] == 0x20 && ipv6[1] == 1 && ipv6[2] == 0 && ipv6[3] <= 0x2f);
    }
}
