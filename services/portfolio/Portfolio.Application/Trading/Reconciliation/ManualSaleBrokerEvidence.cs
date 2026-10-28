using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record ManualSaleBrokerEvidence(
    ManualSaleExecution Execution,
    Balance Balance,
    DateTimeOffset ObservedAt,
    ManualSaleSettlement? Settlement = null,
    ManualSaleProceedsEstimate? ProceedsEstimate = null
);
