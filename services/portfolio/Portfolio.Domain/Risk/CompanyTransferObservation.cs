namespace Quantara.Portfolio.Domain.Risk;

public sealed record CompanyTransferObservation(
    string AccountKey,
    string FlowId,
    string CounterpartyAccountKey,
    string TransferReference
);
