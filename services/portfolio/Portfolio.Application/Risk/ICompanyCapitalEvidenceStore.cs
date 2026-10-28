using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public interface ICompanyCapitalEvidenceStore : IReadOnlyCompanyCapitalEvidenceStore
{
    public Task<CompanyCapitalEvidenceState> Import(
        CompanyCapitalImport import,
        long expectedRevision,
        CancellationToken cancellationToken
    );
}
