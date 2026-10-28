using System.Security.Cryptography;
using System.Text.Json;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingCashBaselineAccounting
{
    public static bool CashUnknown(TradingLedger ledger) =>
        ledger.ExternalSales.Any(sale => sale.Settlement is null && !Covered(ledger, sale));

    public static bool Covered(TradingLedger ledger, TradingExternalSale sale) =>
        ledger.CashBaselines.Any(baseline =>
            sale.Allocation.Execution.OccurredAt <= baseline.Cutoff
            && baseline.CoveredExternalSales.Contains(sale.ReviewReference, StringComparer.Ordinal)
        );

    public static DateTimeOffset? Cutoff(TradingLedger ledger) =>
        ledger.CashBaselines.Count == 0 ? null : ledger.CashBaselines[^1].Cutoff;

    public static bool CashFlowAlreadyIncluded(TradingLedger ledger, CapitalCashFlow flow)
    {
        if (ledger.CashBaselines.Count == 0 || flow.At > ledger.CashBaselines[^1].Cutoff)
        {
            return false;
        }

        var baseline = ledger.CashBaselines[^1];
        // Broker flow timestamps have second precision; concurrent wallet movement is ambiguous.
        if (flow.At >= baseline.BrokerObservedAt.AddSeconds(-1))
        {
            throw new InvalidOperationException(
                "Funding arrived during wallet collection; review current cash again before accounting it."
            );
        }

        return true;
    }

    public static string Binding(TradingState state) =>
        Hash(
            new
            {
                state.Mode,
                state.RulesHash,
                state.Mandate,
                state.EmergencyStopped,
                state.Live.FundingThb,
                state.Live.CashThb,
                state.Live.OpenedAt,
                state.Live.Positions,
                state.Live.Orders,
                state.Live.Fills,
                state.Live.ExternalSales,
                state.Live.CashBaselines,
                state.ObservedFundingFlows,
            }
        );

    public static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();

    public static TradingState Adopt(
        TradingState state,
        TradingCashBaseline baseline,
        string accountKey,
        DateTimeOffset now
    )
    {
        if (
            state.Mode != TradingMode.Stopped
            || state.Mandate is not { RevokedAt: not null }
            || state.Live.Orders.Any(TradingLedgerAccounting.Unresolved)
            || state.Live.Positions.Any(position => position.Quantity > 0)
            || state.Live.ExternalSales.Count == 0
            || baseline.AccountKey != accountKey
            || baseline.PreviousCashThb != state.Live.CashThb
            || baseline.RetainedFundingThb != state.Live.FundingThb
            || baseline.RulesHash != state.RulesHash
            || baseline.MaterialBinding != Binding(state)
            || baseline.RecordedAt > now
            || baseline.BrokerObservedAt > baseline.Cutoff
            || baseline.Cutoff > baseline.RecordedAt
            || now - baseline.BrokerObservedAt > TimeSpan.FromSeconds(30)
            || now >= baseline.ReviewExpiresAt
            || baseline.Cutoff <= Cutoff(state.Live)
            || !baseline
                .CoveredExternalSales.Order(StringComparer.Ordinal)
                .SequenceEqual(
                    state.Live.ExternalSales.Select(sale => sale.ReviewReference).Order(StringComparer.Ordinal)
                )
        )
        {
            throw new InvalidOperationException(
                "Review fresh current cash while stopped, revoked and free of executable positions/orders."
            );
        }

        var ledger = state.Live with
        {
            CashThb = baseline.CashThb,
            CashBaselines = state.Live.CashBaselines.Append(baseline).ToArray(),
            Equity = state
                .Live.Equity.Append(new TradingEquityMark(baseline.RecordedAt, null, state.Live.FundingThb))
                .ToArray(),
        };
        Validate(ledger, accountKey);
        return state with { Revision = checked(state.Revision + 1), Live = ledger, Autonomy = null };
    }

    public static void Validate(TradingLedger ledger, string accountKey)
    {
        if (
            ledger.CashBaselines.Count > 32
            || ledger.CashBaselines.Select(item => item.ReviewReference).Distinct(StringComparer.Ordinal).Count()
                != ledger.CashBaselines.Count
        )
        {
            throw new InvalidDataException("Cash baseline history exceeds its bound or repeats a review.");
        }

        DateTimeOffset? previousCutoff = null;
        foreach (var baseline in ledger.CashBaselines)
        {
            if (
                baseline.AccountKey != accountKey
                || baseline.CashThb < 0
                || baseline.AvailableThb != baseline.CashThb
                || baseline.BrokerObservedAt > baseline.Cutoff
                || baseline.RecordedAt < baseline.Cutoff
                || baseline.RecordedAt - baseline.BrokerObservedAt > TimeSpan.FromSeconds(30)
                || baseline.RecordedAt >= baseline.ReviewExpiresAt
                || baseline.Cutoff <= previousCutoff
                || new[]
                {
                    baseline.RulesHash,
                    baseline.EvidenceHash,
                    baseline.MaterialBinding,
                    baseline.ReviewReference,
                }.Any(hash => hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
                || baseline.CoveredExternalSales.Length is 0 or > 1024
                || baseline.CoveredExternalSales.Distinct(StringComparer.Ordinal).Count()
                    != baseline.CoveredExternalSales.Length
                || baseline.CoveredExternalSales.Any(reference =>
                    !ledger.ExternalSales.Any(sale =>
                        sale.ReviewReference == reference && sale.RecordedAt <= baseline.Cutoff
                    )
                )
            )
            {
                throw new InvalidDataException("Cash baseline evidence is invalid or conflicts with retained history.");
            }

            previousCutoff = baseline.Cutoff;
        }
    }
}
