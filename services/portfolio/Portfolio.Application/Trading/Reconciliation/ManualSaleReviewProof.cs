using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quantara.Portfolio.Application;

// Reuses the existing server owner-control secret; no new credential or persistent preview state.
public sealed class ManualSaleReviewProof(TradingControlOptions options)
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public string Protect(ManualSaleReviewSnapshot snapshot) => this.Protect(snapshot, "");

    public string ProtectCashBaseline(CashBaselineReviewSnapshot snapshot) =>
        this.Protect(snapshot, "cash-baseline-v1:");

    private string Protect<T>(T snapshot, string purpose)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, ManualSaleReviewProof.jsonOptions);
        if (payload.Length > 4500)
        {
            throw new ArgumentException("Manual-sale review exceeds its evidence budget.");
        }

        return Convert.ToBase64String(payload) + "." + Convert.ToHexString(this.Sign(payload, purpose));
    }

    public ManualSaleReviewSnapshot Read(string proof) => this.Read<ManualSaleReviewSnapshot>(proof, "");

    public CashBaselineReviewSnapshot ReadCashBaseline(string proof) =>
        this.Read<CashBaselineReviewSnapshot>(proof, "cash-baseline-v1:");

    private T Read<T>(string proof, string purpose)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(proof) || proof.Length > 7000)
        {
            throw new ArgumentException("Manual-sale review proof is invalid.");
        }

        try
        {
            var parts = proof.Split('.');
            if (parts.Length != 2)
            {
                throw new FormatException();
            }

            var payload = Convert.FromBase64String(parts[0]);
            var signature = Convert.FromHexString(parts[1]);
            if (
                payload.Length > 4500
                || !CryptographicOperations.FixedTimeEquals(signature, this.Sign(payload, purpose))
            )
            {
                throw new FormatException();
            }

            return JsonSerializer.Deserialize<T>(payload, ManualSaleReviewProof.jsonOptions)
                ?? throw new FormatException();
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            throw new ArgumentException("Manual-sale review proof is invalid.");
        }
    }

    private byte[] Sign(byte[] payload, string purpose)
    {
        if (string.IsNullOrWhiteSpace(options.OwnerToken))
        {
            throw new InvalidOperationException(
                "Existing owner controls must be configured for reconciliation review."
            );
        }

        var scoped = purpose.Length == 0 ? payload : Encoding.UTF8.GetBytes(purpose).Concat(payload).ToArray();
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.OwnerToken), scoped);
    }
}
