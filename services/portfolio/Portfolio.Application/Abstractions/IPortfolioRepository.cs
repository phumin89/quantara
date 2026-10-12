using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IPortfolioRepository : IReadOnlyPortfolioRepository
{
    public Task Save(PortfolioLedger ledger, CancellationToken cancellationToken);
}
