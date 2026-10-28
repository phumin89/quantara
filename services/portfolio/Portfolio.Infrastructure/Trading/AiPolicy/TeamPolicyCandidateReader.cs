using System.Net;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TeamPolicyCandidateReader(HttpClient client) : ITradingPolicyCandidateReader
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<TradingPolicyCandidate?> Read(CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "api/team/policy-candidate",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await response.Content.LoadIntoBufferAsync(32768, cancellationToken);
        return JsonSerializer.Deserialize<TradingPolicyCandidate>(
            await response.Content.ReadAsStringAsync(cancellationToken),
            TeamPolicyCandidateReader.jsonOptions
        );
    }
}
