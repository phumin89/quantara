using System.Globalization;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingManualSaleInventory
{
    public static TradingState Retire(
        TradingState state,
        TradingExternalSale sale,
        string accountKey,
        DateTimeOffset now
    )
    {
        if (
            state.Mode != TradingMode.Stopped
            || state.Mandate is { RevokedAt: null } mandate && mandate.ExpiresAt > now
            || state.Live.Orders.Any(TradingLedgerAccounting.Unresolved)
            || sale.WalletObservedAt > now
            || now - sale.WalletObservedAt > TimeSpan.FromSeconds(30)
            || sale.RecordedAt > now
            || state.Live.ExternalSales.Any(previous => previous.ReviewReference == sale.ReviewReference)
            || state.Live.Positions.SingleOrDefault(position => position.Symbol == sale.OriginalPosition.Symbol)
                != sale.OriginalPosition
            || state.Live.Fills.Any(fill =>
                fill.Symbol == sale.Allocation.Execution.Symbol
                && fill.BrokerFillId == sale.Allocation.Execution.ExecutionId
            )
            || state.Live.Orders.Any(order =>
                order.Symbol == sale.Allocation.Execution.Symbol
                && order.BrokerOrderId == sale.Allocation.Execution.OrderId
            )
        )
        {
            throw new InvalidOperationException(
                "Verified empty inventory requires stopped authority, no active orders and an unchanged owned lot."
            );
        }

        var ledger = state.Live with
        {
            Positions = state
                .Live.Positions.Where(position => position.Symbol != sale.OriginalPosition.Symbol)
                .ToArray(),
            ExternalSales = state.Live.ExternalSales.Append(sale).ToArray(),
            CashThb =
                state.Live.CashThb
                + (
                    sale.Allocation.Execution.OccurredAt <= TradingCashBaselineAccounting.Cutoff(state.Live)
                        ? 0m
                        : sale.Settlement?.NetThb ?? 0m
                ),
            Equity = state.Live.Equity.Append(new TradingEquityMark(now, null, state.Live.FundingThb)).ToArray(),
        };
        Validate(ledger, accountKey);
        return state with { Revision = checked(state.Revision + 1), Live = ledger, Autonomy = null };
    }

    public static void Validate(TradingLedger ledger, string accountKey)
    {
        if (
            ledger.ExternalSales.Count > 1024
            || ledger.ExternalSales.Select(sale => sale.Allocation.Lot.LotKey).Distinct(StringComparer.Ordinal).Count()
                != ledger.ExternalSales.Count
            || ledger
                .ExternalSales.Select(sale =>
                    sale.Allocation.Execution.Symbol + ":" + sale.Allocation.Execution.ExecutionId
                )
                .Distinct(StringComparer.Ordinal)
                .Count() != ledger.ExternalSales.Count
            || ledger.ExternalSales.Select(sale => sale.ReviewReference).Distinct(StringComparer.Ordinal).Count()
                != ledger.ExternalSales.Count
        )
        {
            throw new InvalidDataException(
                "External-sale inventory evidence has duplicate identities or exceeds its bound."
            );
        }

        foreach (var sale in ledger.ExternalSales)
        {
            var holding = sale.OriginalPosition;
            var lot = sale.Allocation.Lot;
            var execution = sale.Allocation.Execution;
            _ = ManualSaleAllocationPlanner.Allocate(lot, execution, sale.Allocation.Quantity, [sale.Allocation]);
            if (
                lot.AccountKey != accountKey
                || execution.AccountKey != accountKey
                || lot.LotKey != holding.Symbol + ":" + holding.OpenedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)
                || lot.Symbol != holding.Symbol
                || lot.OpenedAt != holding.OpenedAt
                || lot.RecordedQuantity != holding.Quantity
                || lot.RecordedCostThb != holding.CostThb
                || holding.Quantity <= 0
                || sale.Allocation.Quantity != execution.Quantity
                || sale.Allocation.Quantity <= 0
                || sale.Allocation.Quantity > holding.Quantity
                || execution.Side != TradeSide.Sell
                || execution.Symbol != holding.Symbol
                || execution.OccurredAt < holding.OpenedAt
                || execution.ObservedAt < execution.OccurredAt
                || sale.WalletObservedAt < execution.ObservedAt
                || sale.RecordedAt < sale.WalletObservedAt
                || sale.Wallet.Symbol != holding.Symbol
                || sale.Wallet.Quantity != 0m
                || sale.Wallet.Available != 0m
                || sale.ReviewReference.Length != 64
                || sale.ReviewReference.Any(character => !Uri.IsHexDigit(character))
                || sale.ProceedsEstimate is { } estimate
                    && (
                        estimate.GrossThb <= 0m
                        || estimate.NetFeesThb < 0m
                        || estimate.UnroundedNetThb != estimate.GrossThb - estimate.NetFeesThb
                    )
                || sale.Settlement is { } settlement
                    && (
                        settlement.AccountKey != accountKey
                        || settlement.Symbol != holding.Symbol
                        || settlement.ExecutionId != execution.ExecutionId
                        || settlement.NetThb <= 0m
                        || string.IsNullOrWhiteSpace(settlement.EvidenceReference)
                        || settlement.EvidenceReference.Length > 256
                    )
            )
            {
                throw new InvalidDataException(
                    "External-sale inventory evidence is incomplete or conflicts with its original lot."
                );
            }
        }

        var settlements = ledger
            .ExternalSales.Select(sale => sale.Settlement)
            .OfType<ManualSaleSettlement>()
            .Select(settlement => settlement.EvidenceReference)
            .ToArray();
        if (settlements.Distinct(StringComparer.Ordinal).Count() != settlements.Length)
        {
            throw new InvalidDataException("An external cash credit cannot be counted for more than one sale.");
        }
    }
}
