using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class PortfolioTestSync : IPortfolioSync
{
    public SyncStatus Status => new(false, 1, 1, null);

    public bool Request() => true;
}
