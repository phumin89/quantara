using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using TradingCompany.Domain;

namespace TradingCompany.Application;

/// <summary>Only the durable account-lock owner can persist these changes.</summary>
public sealed partial class LiveAccountTransaction(LiveAccountState initial, Func<LiveAccountState, CancellationToken, Task> persist,
    Func<CancellationToken, Task>? verifyLease = null) : ILiveAccountTransaction
{
    private readonly HashSet<Guid> unsentDispatches = [];
    public LiveAccountState Account { get; private set; } = initial;
    public LiveOrderRecord? ReadOrder(Guid id) => Account.Orders.SingleOrDefault(o => o.Intent.Id == id);
    public Task CommitAsync(CancellationToken cancellationToken) => persist(Account, cancellationToken);
    public Task VerifyLeaseAsync(CancellationToken cancellationToken)
        => verifyLease?.Invoke(cancellationToken) ?? throw new InvalidOperationException("No durable account lease is available.");

    public LiveOrderRecord Reserve(LiveIntent intent, DateTimeOffset now)
    {
        var existing = ReadOrder(intent.Id);
        if (existing is not null)
        {
            if (existing.Intent != intent) throw new InvalidDataException("Intent identity was reused with different content.");
            return existing;
        }
        var decision = LiveRiskPolicy.Evaluate(intent, Account.Snapshot, Account.Mandate, Account.Gate, now);
        if (!CurrentDailyBaseline(now)) decision = new(false, [.. decision.Violations, "DAILY_BASELINE_REQUIRED"]);
        if (Account.Orders.Any(o => o.State is LiveOrderState.Dispatching or LiveOrderState.Unknown))
            decision = new(false, [.. decision.Violations, "UNKNOWN_ORDER_OUTCOME"]);
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(intent)));
        var cash = decision.Allowed && intent.Side == OrderSide.Buy ? intent.Quantity * intent.LimitPrice * (1 + Account.Mandate.FeeReserveRate) : 0;
        var coins = decision.Allowed && intent.Side == OrderSide.Sell ? intent.Quantity : 0;
        var order = new LiveOrderRecord(intent, decision.Allowed ? LiveOrderState.Reserved : LiveOrderState.Rejected,
            null, cash, coins, 0, hash, now, decision.Violations)
        {
            ReservationVersion = Account.Snapshot.Version + 1,
            BrokerCredentialFingerprint = Account.Mandate.CredentialFingerprint
        };
        Account = Account with { Orders = [.. Account.Orders, order] };
        Audit(now, "IntentRecorded", intent.Id, order.State.ToString());
        RecalculateReservations();
        return order;
    }

    public LiveOrderRecord PrepareDispatch(Guid id, PreparedLiveOrder request, DateTimeOffset now)
    {
        var order = ReadOrder(id) ?? throw new InvalidDataException("Unknown local order.");
        if (order.State != LiveOrderState.Reserved || order.PreparedRequest is not null)
            throw new InvalidOperationException("Only an unsent reservation can be prepared.");
        if (request.Intent != order.Intent || string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 65536 ||
            request.BodyHash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.Body))))
            throw new InvalidDataException("Prepared order does not match the approved intent.");
        var violations = DispatchViolations(order, order.ReservationVersion, now);
        if (violations.Count > 0)
        {
            Replace(order with { Violations = violations.Distinct().ToArray() });
            RecordTransition(id, LiveOrderState.Rejected, null, now);
        }
        else
        {
            RecordTransition(id, LiveOrderState.Dispatching, null, now);
            Replace(ReadOrder(id)! with { PreparedRequest = request, DispatchStartedAt = now });
            Audit(now, "DispatchPrepared", id, request.BodyHash);
            unsentDispatches.Add(id);
        }
        return ReadOrder(id)!;
    }

    public LiveOrderRecord ConfirmDispatch(Guid id, DateTimeOffset now)
    {
        var order = ReadOrder(id) ?? throw new InvalidDataException("Unknown local order.");
        // This capability exists only in the transaction that prepared the request. A restored
        // Dispatching record might already have reached the broker and must never be released here.
        if (order.State != LiveOrderState.Dispatching || !unsentDispatches.Remove(id))
            throw new InvalidOperationException("Dispatch is not a known-unsent request in this session.");
        var violations = DispatchViolations(order, order.ReservationVersion + 1, now);
        if (violations.Count > 0)
        {
            Replace(order with { Violations = violations.Distinct().ToArray() });
            RecordTransition(id, LiveOrderState.Rejected, null, now);
        }
        return ReadOrder(id)!;
    }

    private List<string> DispatchViolations(LiveOrderRecord order, long expectedVersion, DateTimeOffset now)
    {
        // Exclude only the current order's reservation; other orders still consume buying power.
        var view = ReservationSnapshot(Account.Snapshot with { Version = order.Intent.SnapshotVersion },
            Account.Orders.Where(o => o.Intent.Id != order.Intent.Id).ToArray());
        var violations = LiveRiskPolicy.Evaluate(order.Intent, view, Account.Mandate, Account.Gate, now).Violations.ToList();
        if (!CurrentDailyBaseline(now)) violations.Add("DAILY_BASELINE_REQUIRED");
        if (Account.Snapshot.Version != expectedVersion) violations.Add("STALE_APPROVAL");
        if (Account.Orders.Any(o => o.Intent.Id != order.Intent.Id && o.State is LiveOrderState.Dispatching or LiveOrderState.Unknown))
            violations.Add("UNKNOWN_ORDER_OUTCOME");
        return violations;
    }

    private bool CurrentDailyBaseline(DateTimeOffset now) => Account.PerformanceBaseline is { } baseline &&
        baseline.BangkokDate == DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime) &&
        baseline.DayStartEquity > 0 && baseline.HighWaterEquity >= baseline.DayStartEquity;

    public void RejectBeforeDispatch(Guid id, string code, DateTimeOffset now)
    {
        var order = ReadOrder(id) ?? throw new InvalidDataException("Unknown local order.");
        if (order.State != LiveOrderState.Reserved) throw new InvalidOperationException("Order may already have been sent.");
        Replace(order with { Violations = [.. order.Violations, code] });
        RecordTransition(id, LiveOrderState.Rejected, null, now);
    }

    public void RequestCancellation(Guid id, DateTimeOffset now)
    {
        var order = ReadOrder(id) ?? throw new InvalidDataException("Unknown local order.");
        if (order.Intent.AccountId != Account.AccountId || order.Intent.Mode != TradingMode.Live ||
            Account.Mandate.AccountId != Account.AccountId || !Account.Gate.IsolationVerified ||
            string.IsNullOrWhiteSpace(Account.Gate.CredentialFingerprint) ||
            Account.Gate.CredentialFingerprint != Account.Mandate.CredentialFingerprint ||
            order.BrokerCredentialFingerprint != Account.Gate.CredentialFingerprint)
            throw new InvalidOperationException("Cancellation account binding is unavailable.");
        if (string.IsNullOrWhiteSpace(order.BrokerId) || order.CancellationRequestedAt is not null ||
            order.State is not (LiveOrderState.Acknowledged or LiveOrderState.Open or LiveOrderState.PartiallyFilled or LiveOrderState.Unknown))
            throw new InvalidOperationException("Order is not eligible for cancellation.");
        Replace(order with { CancellationRequestedAt = now, UpdatedAt = now });
        Account = Account with { Gate = Account.Gate with { Armed = false }, Snapshot = Account.Snapshot with { Reconciled = false } };
        Audit(now, "CancellationRequested", id, order.BrokerId);
        RecalculateReservations();
    }

    public void RecordCancellation(Guid id, BrokerSubmissionState outcome, DateTimeOffset now)
    {
        var order = ReadOrder(id) ?? throw new InvalidDataException("Unknown local order.");
        if (order.CancellationRequestedAt is null || !Enum.IsDefined(outcome))
            throw new InvalidOperationException("Cancellation was not requested.");
        if (order.CancellationOutcome == outcome) return;
        if (order.CancellationOutcome is not null) throw new InvalidOperationException("Cancellation outcome is already recorded.");
        // A successful cancel response does not prove the order had no racing fills.
        // Retain exposure until terminal status, fills and wallet evidence reconcile.
        Replace(order with { CancellationOutcome = outcome, UpdatedAt = now });
        Audit(now, "CancellationResponse", id, outcome.ToString());
    }

    public void SuspendForRecovery(DateTimeOffset now)
    {
        // Recovery is not reconciliation: never arm or infer that an uncertain order did not fill.
        Account = Account with { Gate = Account.Gate with { Armed = false }, Snapshot = Account.Snapshot with { Reconciled = false } };
        unsentDispatches.Clear();
        foreach (var order in Account.Orders.ToArray())
        {
            if (order.State == LiveOrderState.Reserved)
            {
                if (order.PreparedRequest is not null || order.DispatchStartedAt is not null)
                    throw new InvalidDataException("Reserved order contains inconsistent dispatch evidence.");
                RejectBeforeDispatch(order.Intent.Id, "RECOVERED_UNSENT_ORDER", now);
            }
            else if (order.State == LiveOrderState.Dispatching)
                RecordTransition(order.Intent.Id, LiveOrderState.Unknown, null, now);
            if (order.CancellationRequestedAt is not null && order.CancellationOutcome is null)
                RecordCancellation(order.Intent.Id, BrokerSubmissionState.Unknown, now);
        }
        Audit(now, "RecoverySuspended", null, "RECONCILIATION_REQUIRED");
        RecalculateReservations();
    }

    public void RecordTransition(Guid id, LiveOrderState next, string? brokerId, DateTimeOffset now)
    {
        var order = ReadOrder(id) ?? throw new InvalidDataException("Unknown local order.");
        if (next == order.State && (brokerId is null || brokerId == order.BrokerId)) return;
        var allowed = (order.State, next) switch
        {
            (LiveOrderState.Reserved, LiveOrderState.Dispatching or LiveOrderState.Rejected) => true,
            (LiveOrderState.Dispatching, LiveOrderState.Unknown or LiveOrderState.Acknowledged or LiveOrderState.Rejected) => true,
            (LiveOrderState.Unknown, LiveOrderState.Acknowledged) => true,
            (LiveOrderState.Acknowledged, LiveOrderState.Open or LiveOrderState.PartiallyFilled or LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Unknown) => true,
            (LiveOrderState.Open, LiveOrderState.PartiallyFilled or LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Unknown) => true,
            (LiveOrderState.PartiallyFilled, LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Unknown) => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException("Illegal live order transition.");
        if (next == LiveOrderState.Dispatching && (!Account.Gate.Armed || Account.Gate.Stopped || !Account.Gate.IsolationVerified || !Account.Snapshot.Reconciled))
            throw new InvalidOperationException("Live execution gate is closed.");
        var identity = brokerId ?? order.BrokerId;
        if (next == LiveOrderState.Acknowledged && string.IsNullOrWhiteSpace(identity)) throw new InvalidDataException("Broker acknowledgement requires an ID.");
        if (order.BrokerId is not null && identity != order.BrokerId) throw new InvalidDataException("Broker order identity changed.");
        if (identity is not null && Account.Orders.Any(o => o.Intent.Id != id && o.BrokerId == identity &&
            o.Intent.Symbol == order.Intent.Symbol && o.Intent.Side == order.Intent.Side))
            throw new InvalidDataException("Broker order identity is already owned by another intent.");
        if (next == LiveOrderState.Filled && (order.Intent.BuyBudget is null
                ? order.FilledQuantity != order.Intent.Quantity : order.FilledQuantity <= 0))
            throw new InvalidDataException("Filled quantity has not reconciled.");
        var terminal = next is LiveOrderState.Rejected or LiveOrderState.Filled or LiveOrderState.Cancelled;
        Replace(order with { State = next, BrokerId = identity, UpdatedAt = now,
            ReservedCash = terminal ? 0 : order.ReservedCash, ReservedQuantity = terminal ? 0 : order.ReservedQuantity });
        if (next == LiveOrderState.Unknown) Account = Account with { Gate = Account.Gate with { Armed = false }, Snapshot = Account.Snapshot with { Reconciled = false } };
        Audit(now, "OrderState", id, next.ToString());
        RecalculateReservations();
    }

    public void ApplyFill(BrokerFill fill, DateTimeOffset now)
    {
        if (fill.AccountId != Account.AccountId || string.IsNullOrWhiteSpace(fill.TransactionId) || string.IsNullOrWhiteSpace(fill.OrderId) || fill.Revision <= 0 ||
            fill.Quantity <= 0 || fill.Price <= 0 || fill.CashFee < 0 || !Enum.IsDefined(fill.Side)) throw new InvalidDataException("Invalid broker fill.");
        var order = Account.Orders.SingleOrDefault(o => o.BrokerId == fill.OrderId && o.Intent.Symbol == fill.Symbol && o.Intent.Side == fill.Side)
            ?? throw new InvalidDataException("Fill has no owned broker order.");
        if (order.Intent.Side != fill.Side || order.Intent.Symbol != fill.Symbol ||
            order.State is LiveOrderState.Reserved or LiveOrderState.Proposed or LiveOrderState.Rejected)
            throw new InvalidDataException("Fill does not match the owned order.");
        if (fill.QuoteAmount is { } quote && (quote <= 0 || fill.Side != OrderSide.Buy ||
            order.Intent.BuyBudget is null || quote < fill.Quantity * fill.Price))
            throw new InvalidDataException("Unsupported broker quote amount.");
        var existing = Account.Fills.SingleOrDefault(f => f.TransactionId == fill.TransactionId);
        if (existing == fill) return;
        if (existing is not null && (fill.Revision <= existing.Revision || existing with { Revision = fill.Revision, CashFee = fill.CashFee } != fill))
            throw new InvalidDataException("Conflicting fill evidence.");
        var quantity = existing is null ? fill.Quantity : 0m;
        if (order.Intent.BuyBudget is null && order.FilledQuantity + quantity > order.Intent.Quantity)
            throw new InvalidDataException("Fill exceeds order quantity.");
        if (existing is null && ((fill.Side == OrderSide.Buy && fill.Price > order.Intent.LimitPrice) || (fill.Side == OrderSide.Sell && fill.Price < order.Intent.LimitPrice)))
            throw new InvalidDataException("Fill exceeds limit price.");
        var feeChange = fill.CashFee - (existing?.CashFee ?? 0);
        var value = existing is null ? fill.GrossQuoteAmount : 0m;
        if (order.Intent.BuyBudget is { } budget)
        {
            var spent = Account.Fills.Where(f => f.OrderId == order.BrokerId && f.Symbol == order.Intent.Symbol && f.Side == order.Intent.Side)
                .Sum(f => f.GrossQuoteAmount + f.CashFee);
            if (order.Intent.Side != OrderSide.Buy || budget <= 0 || budget != order.Intent.Quantity * order.Intent.LimitPrice ||
                spent + value + feeChange > budget)
                throw new InvalidDataException("Fill exceeds buy cash budget.");
        }
        var cashDelta = (fill.Side == OrderSide.Buy ? -value : value) - feeChange;
        var positions = Account.Snapshot.Positions.ToList();
        var index = positions.FindIndex(p => p.Symbol == fill.Symbol);
        var position = index >= 0 ? positions[index] : new LivePosition(fill.Symbol, 0, 0, fill.Price);
        var newQuantity = position.Quantity + (fill.Side == OrderSide.Buy ? quantity : -quantity);
        if (newQuantity < 0 || Account.Snapshot.CashBalance + cashDelta < 0) throw new InvalidDataException("Fill would overdraw account.");
        if (index >= 0) positions.RemoveAt(index);
        if (newQuantity > 0) positions.Add(position with { Quantity = newQuantity });
        var updatedCash = Account.Snapshot.CashBalance + cashDelta;
        var remainingCash = order.ReservedCash;
        if (fill.Side == OrderSide.Buy && order.State is not (LiveOrderState.Filled or LiveOrderState.Cancelled))
            remainingCash = Math.Max(0, remainingCash - value - feeChange);
        Replace(order with { FilledQuantity = order.FilledQuantity + quantity, ReservedCash = remainingCash,
            ReservedQuantity = Math.Max(0, order.ReservedQuantity - quantity), UpdatedAt = now });
        Account = Account with
        {
            Fills = [.. Account.Fills.Where(f => f.TransactionId != fill.TransactionId), fill],
            Snapshot = Account.Snapshot with { CashBalance = updatedCash, Positions = positions,
                Equity = updatedCash + positions.Sum(p => p.Quantity * p.MarketPrice),
                DailyTurnover = Account.Snapshot.DailyTurnover + value + feeChange, Reconciled = false }
        };
        Audit(now, existing is null ? "FillApplied" : "FeeCorrected", order.Intent.Id, JsonSerializer.Serialize(fill));
        RecalculateReservations();
    }

    public void SetStopped(bool stopped)
    {
        Account = Account with { Gate = Account.Gate with { Stopped = stopped, Armed = false } };
        Audit(Account.Snapshot.ObservedAt, "StopChanged", null, stopped.ToString());
    }

    private void Replace(LiveOrderRecord order) => Account = Account with { Orders = Account.Orders.Select(o => o.Intent.Id == order.Intent.Id ? order : o).ToArray() };
    private void Audit(DateTimeOffset now, string kind, Guid? id, string detail)
        => Account = Account with { Audit = [.. Account.Audit, new(now, kind, id, detail)] };
    private void RecalculateReservations()
        => Account = Account with { Snapshot = ReservationSnapshot(Account.Snapshot with { Version = Account.Snapshot.Version + 1 }, Account.Orders) };

    private static LiveSnapshot ReservationSnapshot(LiveSnapshot snapshot, IReadOnlyList<LiveOrderRecord> allOrders)
    {
        var orders = allOrders.Where(o => o.State is not (LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Rejected)).ToArray();
        return snapshot with {
            CashReserved = orders.Sum(o => o.ReservedCash),
            // Budget orders reserve money, not an exact coin count. Retain the conservative
            // cash reserve until terminal evidence reconciles, including possible fee corrections.
            PendingBuyNotional = orders.Where(o => o.Intent.Side == OrderSide.Buy).Sum(o => o.Intent.BuyBudget is not null
                ? o.ReservedCash : (o.Intent.Quantity - o.FilledQuantity) * o.Intent.LimitPrice),
            PendingSellNotional = orders.Where(o => o.Intent.Side == OrderSide.Sell).Sum(o => (o.Intent.Quantity - o.FilledQuantity) * o.Intent.LimitPrice),
            PendingBuySymbols = orders.Where(o => o.Intent.Side == OrderSide.Buy &&
                (o.Intent.BuyBudget is not null || o.FilledQuantity < o.Intent.Quantity)).Select(o => o.Intent.Symbol).Distinct().ToArray(),
            Positions = snapshot.Positions.Select(p => p with { ReservedQuantity = orders.Where(o => o.Intent.Symbol == p.Symbol).Sum(o => o.ReservedQuantity) }).ToArray()
        };
    }
}
