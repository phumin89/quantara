using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Domain.Trading;

// Retired inventory remains historical evidence, never a synthetic engine fill.
public sealed record TradingExternalSale(
    TradingHolding OriginalPosition,
    ManualSaleAllocation Allocation,
    Balance Wallet,
    DateTimeOffset WalletObservedAt,
    DateTimeOffset RecordedAt,
    string ReviewReference,
    ManualSaleSettlement? Settlement,
    ManualSaleProceedsEstimate? ProceedsEstimate = null
);
