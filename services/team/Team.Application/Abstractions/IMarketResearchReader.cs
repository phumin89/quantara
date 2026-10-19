namespace Quantara.Team.Application;

public interface IMarketResearchReader
{
    public Task<string> Read(string accountKey, CancellationToken cancellationToken);

    public Task<string> ReadForMarkets(
        string accountKey,
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken,
        bool adaptiveTimeframes = false
    ) => this.Read(accountKey, cancellationToken);
}
