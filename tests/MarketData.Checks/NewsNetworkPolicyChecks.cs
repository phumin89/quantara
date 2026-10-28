using System.Net;
using Quantara.MarketData.Infrastructure.News;

namespace Quantara.MarketData.Checks;

public static class NewsNetworkPolicyChecks
{
    public static void Run()
    {
        var allowed = new[]
        {
            "8.8.8.8",
            "192.0.1.1",
            "192.0.1.255",
            "192.0.3.1",
            "192.0.255.254",
            "192.1.0.1",
            "192.88.98.1",
            "192.88.100.1",
            "198.51.99.1",
            "::ffff:192.0.3.1",
            "2001:4860:4860::8888",
            "2001:30::1",
            "2001:3f:ffff::1",
            "2001:200::1",
            "3fff:1000::1",
        };
        var denied = new[]
        {
            "192.0.0.0",
            "192.0.0.255",
            "192.0.2.0",
            "192.0.2.255",
            "192.168.1.1",
            "192.88.99.1",
            "::ffff:192.0.0.1",
            "::ffff:192.0.2.1",
            "::ffff:192.168.1.1",
            "3fff::",
            "3fff::1",
            "3fff:fff:ffff:ffff:ffff:ffff:ffff:ffff",
            "2001:db8::1",
            "2001:40::1",
            "2001:100::1",
            "2001:1ff:ffff:ffff:ffff:ffff:ffff:ffff",
            "2001:3::1",
            "2001:4:112::1",
            "fc00::1",
            "fe80::1",
        };

        foreach (var value in allowed)
        {
            Check(
                NewsNetworkPolicy.IsPublic(IPAddress.Parse(value)),
                "Defensive classifier denied allowed fixture: " + value
            );
        }

        foreach (var value in denied)
        {
            Check(
                !NewsNetworkPolicy.IsPublic(IPAddress.Parse(value)),
                "Defensive classifier allowed denied fixture: " + value
            );
        }

        Console.WriteLine(
            $"News network policy checks passed: {allowed.Length} permitted and {denied.Length} denied literal addresses."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
