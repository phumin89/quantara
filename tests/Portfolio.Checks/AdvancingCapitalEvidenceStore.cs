using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Risk;

namespace Portfolio.Checks;

public sealed class AdvancingCapitalEvidenceStore(ICompanyCapitalEvidenceStore store) : ICompanyCapitalEvidenceStore
{
    public Task<CompanyCapitalEvidenceState> Read(CancellationToken cancellationToken) => store.Read(cancellationToken);

    public Task<CompanyCapitalEvidenceState> Import(
        CompanyCapitalImport import,
        long expectedRevision,
        CancellationToken cancellationToken
    ) => store.Import(import, expectedRevision, cancellationToken);

    public async Task<CompanyCapitalReport?> Report(
        DateTimeOffset observedFrom,
        DateTimeOffset observedTo,
        CancellationToken cancellationToken
    )
    {
        var state = await store.Read(cancellationToken);
        await store.Import(
            state.Receipts.Single().Import with
            {
                Id = "concurrent-observation",
            },
            state.Revision,
            cancellationToken
        );
        return await store.Report(observedFrom, observedTo, cancellationToken);
    }
}
