using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;

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

        object? riskSignal =
            proposal.Side == TradeProposalSide.Buy && proposal.EntryPlan is { } entry
                ? new
                {
                    stopPrice = decimal.Parse(entry.InvalidationPrice, CultureInfo.InvariantCulture),
                    stopCandle = (long?)null,
                    evidenceAt = proposal.EvidenceAt,
                    strategyEvidence = "reviewer-entry-plan",
                }
            : proposal.Side == TradeProposalSide.Sell
                ? new
                {
                    stopPrice = (decimal?)null,
                    stopCandle = (long?)null,
                    evidenceAt = proposal.EvidenceAt,
                    strategyEvidence = "reviewer-exit-thesis",
                }
            : null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/crypto/orders")
        {
            Content = JsonContent.Create(
                new
                {
                    proposal.Symbol,
                    side = proposal.Side.ToString().ToLowerInvariant(),
                    proposal.Amount,
                    proposal.LimitPrice,
                    Reason = TradeProposalDescription.Format(proposal),
                    sourceRunId = runId,
                    expectedAccountKey = accountKey,
                    riskSignal,
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
