namespace Quantara.Portfolio.Domain.Risk;

public sealed record CompanyCapitalEvidenceState(
    int FormatVersion,
    long Revision,
    IReadOnlyList<CompanyBrokerAccount> Accounts,
    IReadOnlyList<CompanyCapitalImportReceipt> Receipts
);
