namespace Quantara.Portfolio.Domain.Risk;

public sealed record CompanyCapitalImport(
    string Id,
    CompanyBrokerAccount Source,
    string AdapterVersion,
    string SourceReference,
    CompanyAccountPeriod Period,
    IReadOnlyList<CompanyTransferObservation> Transfers
);
