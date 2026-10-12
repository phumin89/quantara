using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IReadOnlyPortfolioRepository
{
    public Task<PortfolioLedger> Load(CancellationToken cancellationToken);
}
