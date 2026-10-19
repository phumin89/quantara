using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IPortfolioGoalStore : IReadOnlyPortfolioGoalStore
{
    public Task Save(PortfolioGoalCheckpoint checkpoint, CancellationToken cancellationToken);
}
