using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public interface IAccountEvidenceReader
{
    public Task<Balance[]> ReadBalances(CancellationToken cancellationToken);
}
