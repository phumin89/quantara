using System.Text.Json;
using Quantara.Team.Application;

namespace Team.Checks;

public sealed class RequestedSnapshotReader : IPortfolioSnapshotReader
{
    private static readonly string[] marketSymbols = ["BTC_THB", "KUB_THB"];

    private static readonly decimal[] marketPrices = [100m, 18.99m];

    private static readonly bool[] usableMarkets = [true, true];

    public List<string> Requested { get; } = [];

    public bool ChangeAccount { get; init; }

    public int RefreshedEvidenceCharacters { get; init; }

    public string? RefreshedSnapshot { get; private set; }

    public DateTimeOffset FreshAt { get; } = DateTimeOffset.UtcNow;

    public Task Refresh(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> Read(CancellationToken cancellationToken) => Task.FromResult(this.Snapshot(false));

    public Task<string> ReadForMarkets(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        this.Requested.AddRange(symbols);
        this.RefreshedSnapshot = this.Snapshot(true);
        return Task.FromResult(this.RefreshedSnapshot);
    }

    private string Snapshot(bool refreshed)
    {
        var account = new string(refreshed && this.ChangeAccount ? 'b' : 'a', 64);
        return JsonSerializer.Serialize(
            new
            {
                accountKey = account,
                tradingMode = "manualReview",
                walletEvidence = new
                {
                    accountKey = account,
                    observedAt = this.FreshAt,
                    balances = new[]
                    {
                        new
                        {
                            symbol = "THB",
                            quantity = 1000m,
                            available = 700m,
                        },
                    },
                },
                marketResearch = new
                {
                    requestedDetail = refreshed ? new string('e', this.RefreshedEvidenceCharacters) : string.Empty,
                    available = true,
                    fetchedAt = this.FreshAt,
                    scan = new
                    {
                        symbols = RequestedSnapshotReader.marketSymbols,
                        last = RequestedSnapshotReader.marketPrices,
                        usable = RequestedSnapshotReader.usableMarkets,
                    },
                    markets = new[]
                    {
                        new
                        {
                            symbol = refreshed ? "KUB_THB" : "BTC_THB",
                            last = refreshed ? 18.99m : 100m,
                            bid = refreshed ? 18.98m : 99.9m,
                            ask = refreshed ? 19m : 100.1m,
                        },
                    },
                },
            }
        );
    }
}
