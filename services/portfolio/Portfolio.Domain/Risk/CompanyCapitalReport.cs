namespace Quantara.Portfolio.Domain.Risk;

public sealed record CompanyCapitalReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<string> AccountKeys,
    decimal? OpeningEquityThb,
    decimal? ClosingEquityThb,
    decimal? DepositsThb,
    decimal? WithdrawalsThb,
    decimal? NetContributionsThb,
    decimal? InternalTransferNetThb,
    decimal? InternalTransferFeesThb,
    decimal? NetEquityChangeThb,
    int FlowCount,
    int InternalTransferCount,
    IReadOnlyList<string> Issues
)
{
    public bool VerifiedIncome { get; }

    public string EvidenceOrigin { get; init; } = "unattributed";

    public long? ImportRevision { get; init; }

    public IReadOnlyList<string> ImportDigests { get; init; } = [];

    public string AccountingScope { get; } =
        "Marked equity change after verified external contributions and withdrawals; includes unrealized changes and internal-transfer costs/FX. Not realized trading profit or company income after operating costs.";
}
