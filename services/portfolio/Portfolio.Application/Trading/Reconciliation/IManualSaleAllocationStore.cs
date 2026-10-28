using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface IManualSaleAllocationStore
{
    public Task<ManualSaleAllocationJournal> Read(CancellationToken cancellationToken);

    public Task<bool> Append(
        ManualSaleAllocationReceipt receipt,
        long expectedJournalRevision,
        long expectedTradingRevision,
        CancellationToken cancellationToken,
        TradingExternalSale? retirement = null
    );
}
