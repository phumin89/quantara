using System.Security.Cryptography;
using System.Text;

namespace Quantara.MarketData.Application;

public static class MarketEventAccess
{
    public static bool Allows(string? configured, string? supplied) =>
        !string.IsNullOrWhiteSpace(configured)
        && supplied is { Length: > 0 and <= 256 }
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configured),
            Encoding.UTF8.GetBytes(supplied)
        );
}
