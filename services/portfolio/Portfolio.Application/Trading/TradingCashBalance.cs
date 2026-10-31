using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingCashBalance
{
    public static bool Matches(TradingLedger ledger, decimal brokerCash) =>
        brokerCash >= 0 && ledger.CashThb >= 0 && (brokerCash == ledger.CashThb || brokerCash == SettledCash(ledger));

    public static decimal? SettledCash(TradingLedger ledger)
    {
        var baseline = ledger.CashBaselines.Count == 0 ? null : ledger.CashBaselines[^1];
        if (
            baseline is null
            || baseline.CashThb < 0
            || ledger.FundingThb != baseline.RetainedFundingThb
            || TradingCashBaselineAccounting.CashUnknown(ledger)
            || ledger.ExternalSales.Any(sale => sale.Allocation.Execution.OccurredAt > baseline.Cutoff)
        )
        {
            return null;
        }

        var fills = ledger.Fills.Where(fill => fill.OccurredAt > baseline.Cutoff).ToArray();
        if (fills.Select(fill => fill.BrokerFillId).Distinct(StringComparer.Ordinal).Count() != fills.Length)
        {
            return null;
        }

        var rawCash = baseline.CashThb;
        var settledCash = baseline.CashThb;
        foreach (var fill in fills)
        {
            var orders = ledger.Orders.Where(order => order.Id == fill.IntentId).ToArray();
            if (
                ledger.Fills.Count(other => other.IntentId == fill.IntentId) != 1
                || string.IsNullOrWhiteSpace(fill.BrokerFillId)
                || fill.QuantityScale is null or < 0 or > 18
                || fill.GrossFeeThb is not { } grossFee
                || fill.FeeCreditThb is not { } credit
                || credit < 0
                || credit > grossFee
                || grossFee - credit != fill.FeeThb
                || fill.GrossThb <= 0
                || fill.FeeThb < 0
                || orders.Length != 1
                || orders[0].Symbol != fill.Symbol
                || orders[0].Side != fill.Side
                || orders[0].Status is not ("filled" or "cancelled")
                || orders[0].CancellationState is "claimed" or "unknown"
                || string.IsNullOrWhiteSpace(orders[0].BrokerOrderId)
            )
            {
                return null;
            }

            // Only a single verified execution per completed order is supported.
            // Keep the original fills; statement rounding is not new funding.
            if (fill.Side == TradeSide.Buy)
            {
                var debit = fill.GrossThb + fill.FeeThb;
                rawCash -= debit;
                settledCash -= decimal.Round(debit, 2, MidpointRounding.ToPositiveInfinity);
            }
            else if (fill.Side == TradeSide.Sell && fill.GrossThb >= fill.FeeThb)
            {
                var creditThb = fill.GrossThb - fill.FeeThb;
                rawCash += creditThb;
                settledCash += decimal.Round(creditThb, 2, MidpointRounding.ToZero);
            }
            else
            {
                return null;
            }
        }

        return rawCash == ledger.CashThb && settledCash >= 0 ? settledCash : null;
    }
}
