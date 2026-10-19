using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed class PortfolioTestRepository(PortfolioLedger ledger) : IReadOnlyPortfolioRepository
{
    public Task<PortfolioLedger> Load(CancellationToken cancellationToken) => Task.FromResult(ledger);
}
