using System.Text.Json;
using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed partial class LiveAccountTransaction
{
    public LiveReconciliationResult InitializeBaseline(LiveBaselineRequest request, LiveBrokerEvidence first,
        LiveBrokerEvidence second, DateTimeOffset now)
    {
        if (request.AccountId != Account.AccountId) return FailReconciliation("ACCOUNT_MISMATCH", now);
        if (request.MandateVersion != Account.Mandate.Version || request.SnapshotVersion != Account.Snapshot.Version)
            return FailReconciliation("STALE_APPROVAL", now);
        // Never rebuild a baseline to erase trading losses or absorb a manual deposit.
        if (Account.PerformanceBaseline is not null || Account.FirstActivatedAt is not null)
            return FailReconciliation("BASELINE_ALREADY_ESTABLISHED", now);
        var snapshot = Account.Snapshot;
        if (Account.Gate.Armed || !LiveRiskPolicy.IsValidMandate(Account.Mandate) || Account.Mandate.ExpiresAt <= now ||
            snapshot.Version <= 0 || snapshot.CashBalance <= 0 || snapshot.Equity != snapshot.CashBalance || snapshot.CashReserved != 0 ||
            snapshot.PendingBuyNotional != 0 || snapshot.PendingSellNotional != 0 || snapshot.PendingBuySymbols.Count != 0 ||
            snapshot.Positions.Count != 0 || snapshot.DailyLoss != 0 || snapshot.Drawdown != 0 || snapshot.DailyTurnover != 0 ||
            Account.Fills.Count != 0 || Account.Orders.Any(o => o.State != LiveOrderState.Rejected))
            return FailReconciliation("INITIAL_INVENTORY_NOT_EMPTY", now);

        var candidate = new LiveAccountTransaction(Account with
        {
            PerformanceBaseline = new(DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime),
                snapshot.CashBalance, snapshot.CashBalance),
            Gate = Account.Gate with { Armed = false, ArmedMandateVersion = 0 }
        }, (_, _) => throw new InvalidOperationException("Detached initialization cannot persist."));
        var result = candidate.Reconcile(first, second, now);
        if (!result.Complete) return FailReconciliation(result.Violations[0], now);
        candidate.Audit(now, "InitialPerformanceBaselineEstablished", null, JsonSerializer.Serialize(candidate.Account.PerformanceBaseline));
        Account = candidate.Account;
        return result;
    }

    public LiveReconciliationResult FailReconciliation(string code, DateTimeOffset now)
    {
        Account = Account with
        {
            Gate = Account.Gate with { Armed = false },
            Snapshot = Account.Snapshot with { Reconciled = false, Version = Account.Snapshot.Version + 1 }
        };
        Audit(now, "ReconciliationBlocked", null, code);
        return new(false, Account.Snapshot.Version, [code]);
    }

    public LiveReconciliationResult Reconcile(LiveBrokerEvidence first, LiveBrokerEvidence second, DateTimeOffset now)
    {
        if (!ValidEvidence(first, now) || !ValidEvidence(second, now) || first.ObservedAt > second.ObservedAt)
            return FailReconciliation("BROKER_EVIDENCE_INVALID", now);
        if (Account.Snapshot.AccountId != Account.AccountId || Account.Mandate.AccountId != Account.AccountId ||
            first.AccountId != Account.AccountId || second.AccountId != Account.AccountId ||
            first.CredentialFingerprint != Account.Gate.CredentialFingerprint ||
            second.CredentialFingerprint != Account.Mandate.CredentialFingerprint ||
            first.CredentialFingerprint != second.CredentialFingerprint)
            return FailReconciliation("ACCOUNT_BINDING_CHANGED", now);
        if (!SameMoneyEvidence(first, second)) return FailReconciliation("BROKER_OBSERVATIONS_CHANGED", now);
        var baseline = Account.PerformanceBaseline;
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
        if (baseline is null || baseline.BangkokDate > today || baseline.DayStartEquity <= 0 ||
            baseline.HighWaterEquity < baseline.DayStartEquity)
            return FailReconciliation("DAILY_BASELINE_REQUIRED", now);
        if (baseline.BangkokDate != today)
            return Account.Mandate.AutomaticDailyRollover ? ReconcileAcrossDay(first, second, now, false)
                : FailReconciliation("DAILY_BASELINE_REQUIRED", now);
        if (Account.Orders.Any(o => o.State is LiveOrderState.Reserved or LiveOrderState.Proposed))
            return FailReconciliation("RECOVERY_REQUIRED", now);

        // Stage all accounting against a detached candidate. A failed check cannot leak even
        // an earlier valid fill or terminal reservation release into the durable account.
        var candidate = new LiveAccountTransaction(Account, (_, _) => throw new InvalidOperationException("Detached reconciliation cannot persist."));
        try
        {
            var expected = Account.Orders.Where(o => o.State != LiveOrderState.Rejected).ToArray();
            if (expected.Length != second.Orders.Count) throw new InvalidDataException();
            foreach (var observed in second.Orders)
            {
                var order = expected.SingleOrDefault(o => o.Intent.Id.ToString("N") == observed.ClientId)
                    ?? throw new InvalidDataException();
                if (order.Intent.AccountId != Account.AccountId || order.Intent.Mode != TradingMode.Live ||
                    order.BrokerCredentialFingerprint != second.CredentialFingerprint ||
                    order.Intent.Symbol != observed.Symbol || order.Intent.Side != observed.Side ||
                    order.Intent.Quantity != observed.Quantity || order.Intent.LimitPrice != observed.LimitPrice ||
                    order.Intent.BuyBudget != observed.BuyBudget ||
                    (order.BrokerId is not null && order.BrokerId != observed.BrokerId)) throw new InvalidDataException();
                if (order.State == LiveOrderState.Dispatching)
                    candidate.RecordTransition(order.Intent.Id, LiveOrderState.Unknown, null, now);
                if (candidate.ReadOrder(order.Intent.Id)!.State == LiveOrderState.Unknown)
                    candidate.RecordTransition(order.Intent.Id, LiveOrderState.Acknowledged, observed.BrokerId, now);
                if (order.CancellationRequestedAt is not null && order.CancellationOutcome != BrokerSubmissionState.Rejected &&
                    observed.State is not (LiveOrderState.Filled or LiveOrderState.Cancelled))
                    return FailReconciliation("CANCELLATION_UNRESOLVED", now);
            }

            // Full epoch history must retain old transactions. Corrections have a higher
            // explicit revision; ApplyFill validates identity and permits fee changes only.
            foreach (var fill in second.Fills) candidate.ApplyFill(fill, now);
            if (candidate.Account.Fills.Count != second.Fills.Count) throw new InvalidDataException();
            foreach (var observed in second.Orders)
            {
                var order = candidate.Account.Orders.Single(o => o.Intent.Id.ToString("N") == observed.ClientId);
                if (order.FilledQuantity != observed.FilledQuantity || observed.ReservedCash > order.ReservedCash ||
                    observed.ReservedQuantity > order.ReservedQuantity) throw new InvalidDataException();
                candidate.RecordTransition(order.Intent.Id, observed.State, observed.BrokerId, now);
            }

            var snapshot = candidate.Account.Snapshot;
            if (snapshot.CashBalance != second.CashBalance || second.CashReserved > snapshot.CashReserved ||
                second.CashReserved != second.Orders.Sum(o => o.ReservedCash) ||
                snapshot.Positions.Count != second.Positions.Count) throw new InvalidDataException();
            foreach (var position in second.Positions)
            {
                var local = snapshot.Positions.SingleOrDefault(p => p.Symbol == position.Symbol);
                if (local is null || local.Quantity != position.Quantity || position.ReservedQuantity > local.ReservedQuantity ||
                    position.ReservedQuantity != second.Orders.Where(o => o.Symbol == position.Symbol).Sum(o => o.ReservedQuantity))
                    throw new InvalidDataException();
            }

            var marked = snapshot.Positions.Select(position =>
            {
                var quote = second.Quotes.SingleOrDefault(q => q.Symbol == position.Symbol) ?? throw new InvalidDataException();
                return position with { MarketPrice = quote.Bid };
            }).ToArray();
            var equity = snapshot.CashBalance + marked.Sum(p => p.Quantity * p.MarketPrice);
            var highWater = Math.Max(baseline.HighWaterEquity, equity);
            candidate.Account = candidate.Account with
            {
                PerformanceBaseline = baseline with { HighWaterEquity = highWater },
                Snapshot = snapshot with
                {
                    Positions = marked, Quotes = second.Quotes.ToArray(), Equity = equity,
                    DailyLoss = Math.Max(0, baseline.DayStartEquity - equity),
                    Drawdown = (highWater - equity) / highWater,
                    Reconciled = true, ObservedAt = second.ObservedAt, Version = snapshot.Version + 1
                }
            };
            // Evidence contains no authentication data. Persist it with the accounting changes.
            candidate.Audit(now, "ReconciliationEvidence", null, JsonSerializer.Serialize(second));
            Account = candidate.Account;
            return new(true, Account.Snapshot.Version, []);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or OverflowException or ArgumentException)
        { return FailReconciliation("RECONCILIATION_MISMATCH", now); }
    }

    public LiveReconciliationResult RollDailyBaseline(LiveBaselineRequest request, LiveBrokerEvidence first,
        LiveBrokerEvidence second, DateTimeOffset now)
    {
        if (request.AccountId != Account.AccountId) return FailReconciliation("ACCOUNT_MISMATCH", now);
        if (request.MandateVersion != Account.Mandate.Version || request.SnapshotVersion != Account.Snapshot.Version)
            return FailReconciliation("STALE_APPROVAL", now);
        if (Account.Gate.Armed || Account.PerformanceBaseline is not { } baseline || baseline.DayStartEquity <= 0 ||
            baseline.HighWaterEquity < baseline.DayStartEquity || baseline.BangkokDate >= BangkokDate(now))
            return FailReconciliation("DAILY_BASELINE_REQUIRED", now);
        return ReconcileAcrossDay(first, second, now, true);
    }

    private LiveReconciliationResult ReconcileAcrossDay(LiveBrokerEvidence first, LiveBrokerEvidence second,
        DateTimeOffset now, bool manual)
    {
        var before = Account;
        var baseline = before.PerformanceBaseline!;
        var today = BangkokDate(now);
        var candidate = new LiveAccountTransaction(before with
        {
            PerformanceBaseline = baseline with { BangkokDate = today },
            Gate = before.Gate with { Armed = false }
        }, (_, _) => throw new InvalidOperationException("Detached day reconciliation cannot persist."));
        var result = candidate.Reconcile(first, second, now);
        if (!result.Complete) return FailReconciliation(result.Violations[0], now);
        var allSettled = candidate.Account.Orders.All(o => o.State is LiveOrderState.Rejected or LiveOrderState.Filled or LiveOrderState.Cancelled);
        var fillsUnchanged = candidate.Account.Fills.OrderBy(f => f.TransactionId, StringComparer.Ordinal)
            .SequenceEqual(before.Fills.OrderBy(f => f.TransactionId, StringComparer.Ordinal));
        var continuous = before.Snapshot.Reconciled && before.Snapshot.ObservedAt <= now &&
            now - before.Snapshot.ObservedAt <= TimeSpan.FromSeconds(90) &&
            BangkokDate(before.Snapshot.ObservedAt) == baseline.BangkokDate && baseline.BangkokDate.AddDays(1) == today;
        // Stable evidence may straddle midnight. The second observation must be in the
        // new day; both observations have already passed the exact-money/freshness checks.
        var newDayRead = BangkokDate(second.ObservedAt) == today &&
            (BangkokDate(first.ObservedAt) == today || !manual && BangkokDate(first.ObservedAt) == baseline.BangkokDate);
        var canRoll = allSettled && fillsUnchanged && newDayRead && (manual || continuous);
        var snapshot = candidate.Account.Snapshot;
        if (canRoll)
        {
            var start = manual ? snapshot.Equity : before.Snapshot.Equity;
            if (start <= 0) return FailReconciliation("DAILY_BASELINE_REQUIRED", now);
            candidate.Account = candidate.Account with
            {
                PerformanceBaseline = candidate.Account.PerformanceBaseline! with { DayStartEquity = start },
                Snapshot = snapshot with { DailyTurnover = 0, DailyLoss = Math.Max(0, start - snapshot.Equity) },
                Gate = before.Gate with { Armed = !manual && before.Gate.Armed && !before.Gate.Stopped &&
                    before.Gate.IsolationVerified && before.Gate.ArmedMandateVersion == before.Mandate.Version &&
                    (before.Mandate.ExpiresAt is null || before.Mandate.ExpiresAt > now) }
            };
            candidate.Audit(now, "DailyPerformanceBaselineEstablished", null,
                JsonSerializer.Serialize(new { automatic = !manual, baseline = candidate.Account.PerformanceBaseline }));
            Account = candidate.Account;
            return new(true, snapshot.Version, []);
        }
        // Recover real fills and reservations across midnight even when the daily risk frame
        // cannot be advanced. Preserve the old baseline and require explicit owner action.
        candidate.Account = candidate.Account with
        {
            PerformanceBaseline = baseline with { HighWaterEquity = candidate.Account.PerformanceBaseline!.HighWaterEquity },
            Gate = before.Gate with { Armed = false }
        };
        candidate.Audit(now, "DailyBaselineRequired", null, "Interrupted day or unsettled orders; execution disarmed");
        Account = candidate.Account;
        return new(false, Account.Snapshot.Version, ["DAILY_BASELINE_REQUIRED"]);
    }

    private static DateOnly BangkokDate(DateTimeOffset time) => DateOnly.FromDateTime(time.ToOffset(TimeSpan.FromHours(7)).DateTime);

    private static bool ValidEvidence(LiveBrokerEvidence? evidence, DateTimeOffset now)
    {
        if (evidence is null || !evidence.Complete || evidence.AccountId.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(evidence.CredentialFingerprint) || evidence.ObservedAt > now ||
            now - evidence.ObservedAt > TimeSpan.FromSeconds(30) || evidence.CashBalance < 0 ||
            evidence.CashReserved < 0 || evidence.CashReserved > evidence.CashBalance ||
            evidence.Positions is null || evidence.Orders is null || evidence.Fills is null || evidence.Quotes is null)
            return false;
        if (evidence.Positions.Any(p => p is null || string.IsNullOrWhiteSpace(p.Symbol) || p.Quantity <= 0 ||
                p.ReservedQuantity < 0 || p.ReservedQuantity > p.Quantity) ||
            evidence.Positions.Select(p => p.Symbol).Distinct().Count() != evidence.Positions.Count ||
            evidence.Fills.Any(f => f is null) || evidence.Fills.Select(f => f.TransactionId).Distinct().Count() != evidence.Fills.Count ||
            evidence.Quotes.Any(q => q is null || string.IsNullOrWhiteSpace(q.Symbol) || q.Bid <= 0 || q.Ask < q.Bid ||
                q.ObservedAt > now || now - q.ObservedAt > TimeSpan.FromSeconds(30)) ||
            evidence.Quotes.Select(q => q.Symbol).Distinct().Count() != evidence.Quotes.Count) return false;
        if (evidence.Orders.Any(o => o is null || string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.BrokerId) ||
                string.IsNullOrWhiteSpace(o.Symbol) || !Enum.IsDefined(o.Side) || o.Quantity <= 0 || o.LimitPrice <= 0 ||
                !ValidOrderAmounts(o) || o.FilledQuantity < 0 || o.ReservedCash < 0 || o.ReservedQuantity < 0 ||
                o.State is not (LiveOrderState.Open or LiveOrderState.PartiallyFilled or LiveOrderState.Filled or LiveOrderState.Cancelled) ||
                (o.Side == OrderSide.Buy && o.ReservedQuantity != 0) || (o.Side == OrderSide.Sell && o.ReservedCash != 0) ||
                (o.State is LiveOrderState.Filled or LiveOrderState.Cancelled && (o.ReservedCash != 0 || o.ReservedQuantity != 0)) ||
                (o.State == LiveOrderState.Filled && (o.BuyBudget is null ? o.FilledQuantity != o.Quantity : o.FilledQuantity <= 0)) ||
                (o.State == LiveOrderState.Open && o.FilledQuantity != 0) ||
                (o.State == LiveOrderState.PartiallyFilled && (o.FilledQuantity <= 0 || (o.BuyBudget is null && o.FilledQuantity >= o.Quantity))))) return false;
        return evidence.Orders.Select(o => o.ClientId).Distinct().Count() == evidence.Orders.Count &&
            evidence.Orders.Select(o => (o.Symbol, o.Side, o.BrokerId)).Distinct().Count() == evidence.Orders.Count;
    }

    private static bool ValidOrderAmounts(LiveBrokerOrder order)
    {
        if (order.BuyBudget is not { } budget) return order.FilledQuantity <= order.Quantity;
        try { return order.Side == OrderSide.Buy && budget > 0 && budget == order.Quantity * order.LimitPrice; }
        catch (OverflowException) { return false; }
    }

    private static bool SameMoneyEvidence(LiveBrokerEvidence a, LiveBrokerEvidence b)
        => a.CashBalance == b.CashBalance && a.CashReserved == b.CashReserved &&
            a.Positions.OrderBy(p => p.Symbol, StringComparer.Ordinal).SequenceEqual(b.Positions.OrderBy(p => p.Symbol, StringComparer.Ordinal)) &&
            a.Orders.OrderBy(o => o.ClientId, StringComparer.Ordinal).SequenceEqual(b.Orders.OrderBy(o => o.ClientId, StringComparer.Ordinal)) &&
            a.Fills.OrderBy(f => f.TransactionId, StringComparer.Ordinal).SequenceEqual(b.Fills.OrderBy(f => f.TransactionId, StringComparer.Ordinal));
}
