using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingRuleHash
{
    public static string Compute(TradingRuleSet rules) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rules))))
            .ToLowerInvariant();
}
