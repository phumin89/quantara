using TradingCompany.Domain;

namespace TradingCompany.Application;

// Executor-only normalized evidence. Raw exchange amounts are not valid inputs until their
// quantity/fee semantics have been verified. Complete covers the entire bound-account epoch.
public sealed record LiveBrokerPosition(string Symbol, decimal Quantity, decimal ReservedQuantity);
public sealed record LiveBrokerOrder(string ClientId, string BrokerId, string Symbol, OrderSide Side,
    decimal Quantity, decimal LimitPrice, LiveOrderState State, decimal FilledQuantity,
    decimal ReservedCash, decimal ReservedQuantity)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? BuyBudget { get; init; }
    // Cumulative gross THB proceeds, proven from authenticated net receipt + cash fees.
    public decimal? SettledSellQuoteAmount { get; init; }
}
public sealed record LiveBrokerEvidence(LiveAccountId AccountId, string CredentialFingerprint,
    DateTimeOffset ObservedAt, bool Complete, decimal CashBalance, decimal CashReserved,
    IReadOnlyList<LiveBrokerPosition> Positions, IReadOnlyList<LiveBrokerOrder> Orders,
    IReadOnlyList<BrokerFill> Fills, IReadOnlyList<LiveQuote> Quotes)
{
    // Optional for older evidence. ObservedAt is the wallet request time; retain the
    // start of the bounded REST collection separately, rather than retiming old data.
    public DateTimeOffset? CollectionStartedAt { get; init; }
}
public sealed record LivePerformanceBaseline(DateOnly BangkokDate, decimal DayStartEquity, decimal HighWaterEquity);
public sealed record LiveBaselineRequest(LiveAccountId AccountId, long MandateVersion, long SnapshotVersion);
public sealed record LiveReconciliationResult(bool Complete, long SnapshotVersion, IReadOnlyList<string> Violations);

public interface ILiveReconciliationSource
{
    Task<LiveBrokerEvidence> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken);
}

public sealed class LiveReconciliation(ILiveTradingStore store, ILiveReconciliationSource source, TimeProvider clock)
{
    public Task<LiveReconciliationResult> ReconcileAsync(LiveAccountId accountId, CancellationToken cancellationToken = default)
        => ReadAndReconcile(accountId, null, cancellationToken);

    // Owner onboarding only. No owner-supplied balances and no automatic day/loss reset.
    public Task<LiveReconciliationResult> InitializeBaselineAsync(LiveBaselineRequest request, CancellationToken cancellationToken = default)
        => ReadAndReconcile(request.AccountId, request, cancellationToken);
    public Task<LiveReconciliationResult> InitializeBaselineForOwnerCommandAsync(LiveAccountId accountId,
        long mandateVersion, CancellationToken cancellationToken = default)
        => ReadAndReconcile(accountId, new(accountId, mandateVersion, 0), cancellationToken, currentSnapshot: true);
    public Task<LiveReconciliationResult> RollDailyBaselineAsync(LiveBaselineRequest request, CancellationToken cancellationToken = default)
        => ReadAndReconcile(request.AccountId, request, cancellationToken, manualDay: true);

    // Explicit local owner recovery command: keep its approved mandate pinned, but
    // capture the current snapshot after acquiring ownership. UI approvals retain
    // their original strict snapshot versions through the existing method.
    public Task<LiveReconciliationResult> RollDailyBaselineForOwnerCommandAsync(LiveAccountId accountId,
        long mandateVersion, CancellationToken cancellationToken = default)
        => ReadAndReconcile(accountId, new(accountId, mandateVersion, 0), cancellationToken,
            manualDay: true, currentSnapshot: true);

    private Task<LiveReconciliationResult> ReadAndReconcile(LiveAccountId accountId, LiveBaselineRequest? initialization,
        CancellationToken cancellationToken, bool manualDay = false, bool currentSnapshot = false)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            if (currentSnapshot && initialization is not null)
                initialization = initialization with { SnapshotVersion = tx.Account.Snapshot.Version };
            var result = await ReadUnderLease(tx, accountId, initialization, ct, manualDay);
            await Persist(tx);
            return result;
        }, cancellationToken);

    // Owner activation keeps reconciliation and Arm in this same lease. No nested
    // acquisition or intermediate commit lets a failed background read intervene.
    internal async Task<LiveReconciliationResult> PrepareOwnerActivationUnderLeaseAsync(ILiveAccountTransaction tx,
        CancellationToken cancellationToken)
    {
        var account = tx.Account;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
        var manualDay = account.PerformanceBaseline is { } baseline && baseline.BangkokDate < today &&
            account.Orders.All(o => o.State is LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Rejected);
        var request = account.PerformanceBaseline is null || manualDay
            ? new LiveBaselineRequest(account.AccountId, account.Mandate.Version, account.Snapshot.Version) : null;
        var result = await ReadUnderLease(tx, account.AccountId, request, cancellationToken, manualDay);
        if (!result.Complete && result.Violations.SequenceEqual(new[] { "DAILY_BASELINE_REQUIRED" }) &&
            tx.Account.Snapshot.Reconciled && tx.Account.PerformanceBaseline is { } recovered && recovered.BangkokDate < today &&
            tx.Account.Orders.All(o => o.State is LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Rejected))
            result = await ReadUnderLease(tx, account.AccountId,
                new(account.AccountId, account.Mandate.Version, tx.Account.Snapshot.Version), cancellationToken, manualDay: true);
        return result;
    }

    private async Task<LiveReconciliationResult> ReadUnderLease(ILiveAccountTransaction tx, LiveAccountId accountId,
        LiveBaselineRequest? initialization, CancellationToken cancellationToken, bool manualDay = false)
    {
        LiveBrokerEvidence first;
        LiveBrokerEvidence second;
        try
        {
            first = Freeze(await source.ReadAsync(accountId, cancellationToken));
            second = Freeze(await source.ReadAsync(accountId, cancellationToken));
        }
        catch (Exception)
        { return tx.FailReconciliation("BROKER_EVIDENCE_UNAVAILABLE", clock.GetUtcNow()); }
        if (initialization is not null) await tx.VerifyLeaseAsync(cancellationToken);
        return manualDay ? tx.RollDailyBaseline(initialization!, first, second, clock.GetUtcNow()) : initialization is null
            ? tx.Reconcile(first, second, clock.GetUtcNow())
            : tx.InitializeBaseline(initialization, first, second, clock.GetUtcNow());
    }

    private static LiveBrokerEvidence Freeze(LiveBrokerEvidence evidence)
        => evidence with { Positions = evidence.Positions.ToArray(), Orders = evidence.Orders.ToArray(),
            Fills = evidence.Fills.ToArray(), Quotes = evidence.Quotes.ToArray() };

    private static async Task Persist(ILiveAccountTransaction transaction)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await transaction.CommitAsync(timeout.Token);
    }
}
