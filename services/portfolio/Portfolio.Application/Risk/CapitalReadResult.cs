using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed record CapitalReadResult(
    CapitalSnapshot Snapshot,
    RiskAccountEvidence Evidence,
    IReadOnlyList<TradingBook> Markets
);
