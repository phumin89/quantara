using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public interface IReadOnlyCompanyCapitalEvidenceStore
{
    public Task<CompanyCapitalEvidenceState> Read(CancellationToken cancellationToken);

    public Task<CompanyCapitalReport?> Report(
        DateTimeOffset observedFrom,
        DateTimeOffset observedTo,
        CancellationToken cancellationToken
    );
}
