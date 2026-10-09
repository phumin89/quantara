using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record LiveOrderRecord(LiveIntent Intent, LiveOrderState State, string? BrokerId,
    decimal ReservedCash, decimal ReservedQuantity, decimal FilledQuantity, string RequestHash,
    DateTimeOffset UpdatedAt, IReadOnlyList<string> Violations)
{
    public long ReservationVersion { get; init; }
    public PreparedLiveOrder? PreparedRequest { get; init; }
    public DateTimeOffset? DispatchStartedAt { get; init; }
    public DateTimeOffset? CancellationRequestedAt { get; init; }
    public BrokerSubmissionState? CancellationOutcome { get; init; }
    public string? BrokerCredentialFingerprint { get; init; }
    // Cumulative THB settlement differs from exact fill quantity * price by cash precision.
    // Keep this on the order: a cumulative receipt cannot prove each fill's cash allocation.
    public decimal SellQuoteAdjustment { get; init; }
}
public sealed record PreparedLiveOrder(LiveIntent Intent, string Body, string BodyHash);
public enum BrokerSubmissionState { Accepted, Rejected, Unknown }
public sealed record BrokerSubmission(BrokerSubmissionState State, string? BrokerId, string EvidenceCode);
public sealed record BrokerCancellation(BrokerSubmissionState State, string EvidenceCode);

public interface ILiveBroker
{
    // Preparation is read-only. Placement and cancellation are executor-only side effects.
    Task<PreparedLiveOrder> PrepareAsync(LiveIntent intent, CancellationToken cancellationToken);
    Task<BrokerSubmission> PlaceAsync(PreparedLiveOrder request, CancellationToken cancellationToken);
    Task<BrokerCancellation> CancelAsync(string symbol, string orderId, OrderSide side, CancellationToken cancellationToken);
}
public sealed record BrokerFill(LiveAccountId AccountId, string TransactionId, string OrderId, OrderSide Side,
    string Symbol, decimal Quantity, decimal Price, decimal CashFee, long Revision)
{
    // Broker quote spending can differ from rounded received coins times the fill price.
    // Null preserves the exact-price accounting used by existing persisted fills.
    public decimal? QuoteAmount { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal GrossQuoteAmount => QuoteAmount ?? Quantity * Price;
}
public sealed record LiveAuditEvent(DateTimeOffset At, string Kind, Guid? IntentId, string Detail);
// Authenticated wallet inventory at onboarding, not an application trade or profit.
// Immutable after account creation; reconciliation must never absorb later deposits.
public sealed record LiveOpeningBalance(DateTimeOffset ObservedAt, string CredentialFingerprint,
    string BrokerAccountReference, decimal CashBalance, IReadOnlyList<LiveBrokerPosition> Positions);
public sealed record LiveAccountState(LiveAccountId AccountId, LiveMandate Mandate, LiveGateState Gate,
    LiveSnapshot Snapshot, IReadOnlyList<LiveOrderRecord> Orders, IReadOnlyList<BrokerFill> Fills, IReadOnlyList<LiveAuditEvent> Audit)
{
    public LivePerformanceBaseline? PerformanceBaseline { get; init; }
    public DateTimeOffset? FirstActivatedAt { get; init; }
    public string? BrokerAccountReference { get; init; }
    public LiveOpeningBalance? OpeningBalance { get; init; }
}

public interface ILiveTradingStore
{
    Task<LiveSnapshot> ReadAsync(LiveAccountId id, CancellationToken cancellationToken = default);
    Task<LiveAccountState> ReadAccountAsync(LiveAccountId id, CancellationToken cancellationToken = default);
    Task<T> WithAccountLockAsync<T>(LiveAccountId id, Func<ILiveAccountTransaction, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}

public interface ILiveAccountTransaction
{
    LiveAccountState Account { get; }
    LiveGateDecision SaveMandate(LiveMandate mandate, DateTimeOffset now);
    LiveGateDecision Arm(LiveArmRequest request, LiveActivationEvidence evidence, DateTimeOffset now);
    LiveGateDecision AssessActivation(LiveArmRequest request, LiveActivationEvidence evidence, DateTimeOffset now);
    LiveOrderRecord? ReadOrder(Guid id);
    LiveOrderRecord Reserve(LiveIntent intent, DateTimeOffset now);
    LiveOrderRecord PrepareDispatch(Guid id, PreparedLiveOrder request, DateTimeOffset now);
    LiveOrderRecord ConfirmDispatch(Guid id, DateTimeOffset now);
    void RejectBeforeDispatch(Guid id, string code, DateTimeOffset now);
    void RequestCancellation(Guid id, DateTimeOffset now);
    void RecordCancellation(Guid id, BrokerSubmissionState outcome, DateTimeOffset now);
    void SuspendForRecovery(DateTimeOffset now);
    LiveReconciliationResult Reconcile(LiveBrokerEvidence first, LiveBrokerEvidence second, DateTimeOffset now);
    LiveReconciliationResult InitializeBaseline(LiveBaselineRequest request, LiveBrokerEvidence first, LiveBrokerEvidence second, DateTimeOffset now);
    LiveReconciliationResult RollDailyBaseline(LiveBaselineRequest request, LiveBrokerEvidence first, LiveBrokerEvidence second, DateTimeOffset now);
    LiveReconciliationResult FailReconciliation(string code, DateTimeOffset now);
    void RecordTransition(Guid id, LiveOrderState state, string? brokerId, DateTimeOffset now);
    void ApplyFill(BrokerFill fill, DateTimeOffset now);
    void SetStopped(bool stopped);
    void RecordMeeting(DailyMeetingAttempt attempt, DateTimeOffset now);
    LiveTradePlanReceipt RecordTradePlan(LiveTradePlanRequest plan, LiveOrderSizing sizing, DateTimeOffset now)
        => throw new NotSupportedException("Trade plans require a durable account transaction.");
    void InvalidateTradePlan(Guid id, DateTimeOffset now)
        => throw new NotSupportedException("Trade plans require a durable account transaction.");
    Task CommitAsync(CancellationToken cancellationToken);
    Task VerifyLeaseAsync(CancellationToken cancellationToken);
}
