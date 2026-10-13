using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed class FakeAccountEvidenceReader(Balance[] balances) : IAccountEvidenceReader
{
    public Task<Balance[]> ReadBalances(CancellationToken cancellationToken) => Task.FromResult(balances);
}
