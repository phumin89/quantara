using System.Text.Json.Nodes;
using Quantara.Team.Application;

namespace Team.Checks;

public sealed class MutableResearchSnapshotReader : IPortfolioSnapshotReader
{
    private readonly FakePortfolioSnapshotReader source = new() { TradingMode = "autoLive" };

    public string? Mutation { get; set; }

    public string? RequestedMutation { get; init; }

    public bool EmptyHoldings { get; init; }

    public Action? OnRequestedRead { get; init; }

    public int RequestedCount { get; private set; }

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
        if (this.EmptyHoldings)
        {
            coin["quantity"] = 0;
            coin["available"] = 0;
        }

        if (this.Mutation == "quantity")
        {
            coin["quantity"] = 2;
        }

        if (this.Mutation == "available")
        {
            coin["available"] = 0.5m;
        }

        if (this.RequestedCount > 0)
        {
            var market =
                root["marketResearch"]?["markets"]?[0]
                ?? throw new InvalidOperationException("Fixture market missing.");
            market["last"] = 101m;
        }

        return root.ToJsonString();
    }

    public Task<string> ReadForMarkets(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        this.RequestedCount++;
        this.Mutation = this.RequestedMutation;
        this.OnRequestedRead?.Invoke();
        return this.Read(cancellationToken);
    }
}
