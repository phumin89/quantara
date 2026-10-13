using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class OrderProposalPublisher(HttpClient http) : IOrderProposalPublisher
{
    public async Task Publish(
        Guid runId,
        string accountKey,
        TradeProposal proposal,
        CancellationToken cancellationToken
    )
    {
        var now = DateTimeOffset.UtcNow;
        if (proposal.EvidenceAt > now.AddSeconds(30) || now - proposal.EvidenceAt > TimeSpan.FromMinutes(10))
        {
            throw new InvalidOperationException("Proposal evidence is stale. Fresh research is required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/crypto/orders")
        {
            Content = JsonContent.Create(
                new
                {
                    proposal.Symbol,
                    side = proposal.Side.ToString().ToLowerInvariant(),
                    proposal.Amount,
                    proposal.LimitPrice,
                    proposal.Reason,
                    sourceRunId = runId,
                    expectedAccountKey = accountKey,
                }
            ),
        };
        request.Headers.Add("X-Quantara-Order-Action", "review");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
        if (document.RootElement.GetProperty("id").GetGuid() != runId)
        {
            throw new InvalidOperationException("Proposal response did not match its source task.");
        }
    }
}
