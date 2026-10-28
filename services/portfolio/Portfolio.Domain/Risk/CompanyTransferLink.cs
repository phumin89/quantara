namespace Quantara.Portfolio.Domain.Risk;

public sealed record CompanyTransferLink(
    string WithdrawalAccountKey,
    string WithdrawalFlowId,
    string DepositAccountKey,
    string DepositFlowId,
    string EvidenceReference
);
