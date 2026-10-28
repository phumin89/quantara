using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record ManualSaleReviewResult(
    string Symbol,
    decimal AllocatedSoldQuantity,
    decimal UnallocatedHistoricalQuantity,
    decimal? AllocatedCostThb,
    decimal? RealizedNetProfitThb,
    Balance BrokerBalance,
    DateTimeOffset BrokerObservedAt,
    DateTimeOffset ExpiresAt,
    string Proof,
    string Acknowledgement,
    bool Applied,
    bool CashSettlementPending,
    ManualSaleProceedsEstimate? ProceedsEstimate = null
);
