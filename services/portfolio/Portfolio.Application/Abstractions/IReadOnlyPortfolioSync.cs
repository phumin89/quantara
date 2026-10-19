namespace Quantara.Portfolio.Application;

public interface IReadOnlyPortfolioSync
{
    public SyncStatus Status { get; }
}
