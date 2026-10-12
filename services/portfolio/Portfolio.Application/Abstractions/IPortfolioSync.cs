namespace Quantara.Portfolio.Application;

public interface IPortfolioSync
{
    public SyncStatus Status { get; }

    public bool Request();
}
