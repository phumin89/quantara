using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class CashBaselineReviewService(
    IAccountContext account,
    ITradingStore trading,
    IReadOnlyOrderStore orders,
    IRiskEvidenceReader broker,
    ManualSaleReviewProof proofs,
    TimeProvider time,
    SemaphoreSlim gate
) : ICashBaselineReview
{
    public const string Acknowledgement =
        "Replace executable cash with this verified broker balance, not sale proceeds or funding. Retain historical unknown profit, daily losses and risk limits. Keep trading stopped and revoked.";

    public async Task<CashBaselineReviewResult> Preview(CancellationToken cancellationToken)
    {
        var state = await trading.Read(cancellationToken);
        await this.ValidateState(state, cancellationToken);
        var evidence = await broker.Read(cancellationToken);
        var cash = this.ValidateEvidence(state, evidence);
        var now = time.GetUtcNow();
        var snapshot = new CashBaselineReviewSnapshot(
            account.AccountKey,
            cash.Quantity,
            cash.Available ?? throw new InvalidDataException("Current available THB is unknown."),
            evidence.ObservedAt,
            EvidenceHash(evidence),
            TradingCashBaselineAccounting.Binding(state),
            state.Revision,
            now,
            now.AddMinutes(5)
        );
        return Result(snapshot, proofs.ProtectCashBaseline(snapshot), false, state, now);
    }

    public async Task<CashBaselineReviewResult> Apply(
        string proof,
        string acknowledgement,
        CancellationToken cancellationToken
    )
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await this.ApplyReviewed(proof, acknowledgement, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CashBaselineReviewResult> ApplyReviewed(
        string proof,
        string acknowledgement,
        CancellationToken cancellationToken
    )
    {
        if (acknowledgement != Acknowledgement)
        {
            throw new ArgumentException("Review the exact broker cash and retained accounting uncertainty.");
        }

        var snapshot = proofs.ReadCashBaseline(proof);
        if (snapshot.AccountKey != account.AccountKey)
        {
            throw new ArgumentException("Cash review belongs to another account.");
        }

        var reference = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))).ToLowerInvariant();
        var state = await trading.Read(cancellationToken);
        var retained = state.Live.CashBaselines.SingleOrDefault(item => item.ReviewReference == reference);
        if (retained is not null)
        {
            if (
                retained.EvidenceHash != snapshot.EvidenceHash
                || retained.CashThb != snapshot.CashThb
                || retained.MaterialBinding != snapshot.MaterialBinding
            )
            {
                throw new InvalidDataException("Retained cash review conflicts with its proof.");
            }

            return Result(
                snapshot with
                {
                    BrokerObservedAt = retained.BrokerObservedAt,
                },
                proof,
                true,
                state,
                time.GetUtcNow()
            );
        }

        var now = time.GetUtcNow();
        if (
            snapshot.IssuedAt > now
            || now >= snapshot.ExpiresAt
            || snapshot.ExpiresAt - snapshot.IssuedAt != TimeSpan.FromMinutes(5)
            || snapshot.MaterialBinding != TradingCashBaselineAccounting.Binding(state)
        )
        {
            throw new InvalidOperationException(
                "Cash review expired or material state changed; request a new preview."
            );
        }

        await this.ValidateState(state, cancellationToken);
        var fresh = await broker.Read(cancellationToken);
        var collectedAt = time.GetUtcNow();
        var cash = this.ValidateEvidence(state, fresh);
        if (
            cash.Quantity != snapshot.CashThb
            || cash.Available != snapshot.AvailableThb
            || EvidenceHash(fresh) != snapshot.EvidenceHash
                && EvidenceHash(fresh, canonical: false) != snapshot.EvidenceHash
        )
        {
            throw new InvalidOperationException("Broker balances, orders or funding evidence changed; review again.");
        }

        var current = await trading.Read(cancellationToken);
        await this.ValidateState(current, cancellationToken);
        now = time.GetUtcNow();
        if (TradingCashBaselineAccounting.Binding(current) != snapshot.MaterialBinding)
        {
            throw new InvalidOperationException("Material state changed during cash verification.");
        }

        var baseline = new TradingCashBaseline(
            account.AccountKey,
            current.Live.CashThb,
            cash.Quantity,
            cash.Available ?? throw new InvalidDataException("Current available THB is unknown."),
            current.Live.FundingThb,
            fresh.ObservedAt,
            collectedAt,
            now,
            snapshot.ExpiresAt,
            current.RulesHash,
            snapshot.EvidenceHash,
            snapshot.MaterialBinding,
            reference,
            current.Live.ExternalSales.Select(sale => sale.ReviewReference).ToArray()
        );
        var updated = TradingCashBaselineAccounting.Adopt(current, baseline, account.AccountKey, now);
        if (!await trading.Save(updated, current.Revision, cancellationToken))
        {
            throw new InvalidOperationException(
                "Trading revision changed; cash was not adopted. Request a new preview."
            );
        }

        return Result(snapshot with { BrokerObservedAt = fresh.ObservedAt }, proof, true, updated, now);
    }

    private async Task ValidateState(TradingState state, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (
            state.Mode != TradingMode.Stopped
            || state.Mandate is not { RevokedAt: not null }
            || state.Live.Orders.Any(TradingLedgerAccounting.Unresolved)
            || state.Live.Positions.Any(position => position.Quantity > 0)
            || state.Live.ExternalSales.Count == 0
            || (await orders.Exposure(now, cancellationToken)).Any(order =>
                order.State
                    is OrderState.Submitting
                        or OrderState.Unknown
                        or OrderState.Submitted
                        or OrderState.PartiallyFilled
                || order.State == OrderState.Pending && order.ExpiresAt > now
            )
        )
        {
            throw new InvalidOperationException(
                "Retire verified sold inventory first; require stopped/revoked authority and no active orders."
            );
        }
    }

    private Balance ValidateEvidence(TradingState state, RiskAccountEvidence evidence)
    {
        var now = time.GetUtcNow();
        if (
            evidence.ObservedAt > now
            || now - evidence.ObservedAt > TimeSpan.FromSeconds(30)
            || evidence.OpenOrders.Length != 0
            || evidence.Balances.Select(balance => balance.Symbol).Distinct(StringComparer.Ordinal).Count()
                != evidence.Balances.Length
            || evidence.Balances.Any(balance =>
                balance.Quantity < 0 || balance.Available is null || balance.Available != balance.Quantity
            )
            || state.Live.ExternalSales.Any(sale =>
                !evidence.Balances.Any(balance =>
                    balance.Symbol == sale.OriginalPosition.Symbol && balance.Quantity == 0m && balance.Available == 0m
                )
            )
        )
        {
            throw new InvalidOperationException(
                "Fresh coherent account-wide balances, zero retired inventory and no open/reserved orders are required."
            );
        }

        return evidence.Balances.SingleOrDefault(balance => balance.Symbol == "THB")
            ?? throw new InvalidOperationException("Current THB is unknown; no baseline can be adopted.");
    }

    private static string EvidenceHash(RiskAccountEvidence evidence, bool canonical = true) =>
        TradingCashBaselineAccounting.Hash(
            new
            {
                Balances = evidence
                    .Balances.OrderBy(balance => balance.Symbol, StringComparer.Ordinal)
                    .Select(balance =>
                        canonical
                            ? balance with
                            {
                                Quantity = CanonicalNumber(balance.Quantity),
                                Available = balance.Available is { } available ? CanonicalNumber(available) : null,
                            }
                            : balance
                    )
                    .ToArray(),
                evidence.OpenOrders,
                CashFlows = evidence
                    .CashFlows.OrderBy(flow => flow.Id, StringComparer.Ordinal)
                    .Select(flow =>
                        canonical
                            ? flow with
                            {
                                Quantity = CanonicalNumber(flow.Quantity),
                                FeeQuantity = CanonicalNumber(flow.FeeQuantity),
                                ValueThb = flow.ValueThb is { } value ? CanonicalNumber(value) : null,
                                At = flow.At.ToUniversalTime(),
                            }
                            : flow
                    )
                    .ToArray(),
            }
        );

    // Remove decimal scale differences without converting financial values to floating point.
    private static decimal CanonicalNumber(decimal value) =>
        decimal.Parse(
            value.ToString("G29", CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture
        );

    private static CashBaselineReviewResult Result(
        CashBaselineReviewSnapshot snapshot,
        string proof,
        bool applied,
        TradingState state,
        DateTimeOffset now
    ) =>
        new(
            snapshot.AccountKey,
            snapshot.CashThb,
            snapshot.AvailableThb,
            snapshot.BrokerObservedAt,
            snapshot.ExpiresAt,
            proof,
            Acknowledgement,
            applied,
            state.Live.ExternalSales.Any(sale =>
                sale.Allocation.Execution.OccurredAt.ToOffset(TimeSpan.FromHours(7)).Date
                == now.ToOffset(TimeSpan.FromHours(7)).Date
            )
                || state.Live.Fills.Any(fill =>
                    fill.Side == TradeSide.Sell
                    && fill.RealizedNetThb is null
                    && fill.OccurredAt.ToOffset(TimeSpan.FromHours(7)).Date == now.ToOffset(TimeSpan.FromHours(7)).Date
                )
        );
}
