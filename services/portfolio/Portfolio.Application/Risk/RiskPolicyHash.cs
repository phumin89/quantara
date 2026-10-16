using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public static class RiskPolicyHash
{
    public static string Compute(RiskPolicy policy) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(policy))))
            .ToLowerInvariant();

    public static RiskPolicyState Default() => new(Compute(new RiskPolicy()), new RiskPolicy());
}
