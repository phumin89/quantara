using System.Text.Json.Nodes;
using Quantara.Team.Application;

namespace Team.Checks;

public sealed class MutableResearchSnapshotReader : IPortfolioSnapshotReader
{
    private readonly FakePortfolioSnapshotReader source = new() { TradingMode = "autoLive" };

    public string? Mutation { get; set; }

    public Task Refresh(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<string> Read(CancellationToken cancellationToken)
    {
        var root =
            JsonNode.Parse(await this.source.Read(cancellationToken))?.AsObject()
            ?? throw new InvalidOperationException("Fixture missing.");
        root["policyHash"] = this.Mutation == "policy" ? "policy-b" : "policy-a";
        if (this.Mutation == "account")
        {
            root["accountKey"] = new string('b', 64);
        }

        if (this.Mutation == "mode")
        {
            root["tradingMode"] = "manualReview";
        }

        var coin =
            root["walletEvidence"]?["balances"]?[1] ?? throw new InvalidOperationException("Fixture balance missing.");
        if (this.Mutation == "quantity")
        {
            coin["quantity"] = 2;
        }

        if (this.Mutation == "available")
        {
            coin["available"] = 0.5m;
        }

        return root.ToJsonString();
    }
}
