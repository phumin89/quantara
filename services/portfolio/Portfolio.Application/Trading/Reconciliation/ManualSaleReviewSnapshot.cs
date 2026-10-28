using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record ManualSaleReviewSnapshot(
    ManualSaleLot Lot,
    ManualSaleExecution Execution,
    decimal Quantity,
    Balance Balance,
    DateTimeOffset BrokerObservedAt,
    long TradingRevision,
    string TradingBinding,
    long JournalRevision,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    ManualSaleSettlement? Settlement = null,
    ManualSaleProceedsEstimate? ProceedsEstimate = null
);
