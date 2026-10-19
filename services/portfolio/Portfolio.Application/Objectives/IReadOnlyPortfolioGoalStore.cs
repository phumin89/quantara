using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IReadOnlyPortfolioGoalStore
{
    public Task<PortfolioGoalCheckpoint?> Read(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
