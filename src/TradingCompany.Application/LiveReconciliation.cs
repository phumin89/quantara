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
}
public sealed record LiveBrokerEvidence(LiveAccountId AccountId, string CredentialFingerprint,
    DateTimeOffset ObservedAt, bool Complete, decimal CashBalance, decimal CashReserved,
    IReadOnlyList<LiveBrokerPosition> Positions, IReadOnlyList<LiveBrokerOrder> Orders,
    IReadOnlyList<BrokerFill> Fills, IReadOnlyList<LiveQuote> Quotes);
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
    public Task<LiveReconciliationResult> RollDailyBaselineAsync(LiveBaselineRequest request, CancellationToken cancellationToken = default)
        => ReadAndReconcile(request.AccountId, request, cancellationToken, manualDay: true);

    private Task<LiveReconciliationResult> ReadAndReconcile(LiveAccountId accountId, LiveBaselineRequest? initialization,
        CancellationToken cancellationToken, bool manualDay = false)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            LiveBrokerEvidence first;
            LiveBrokerEvidence second;
            try
            {
                first = Freeze(await source.ReadAsync(accountId, ct));
                second = Freeze(await source.ReadAsync(accountId, ct));
            }
            catch (Exception)
            {
                var blocked = tx.FailReconciliation("BROKER_EVIDENCE_UNAVAILABLE", clock.GetUtcNow());
                await Persist(tx);
                return blocked;
            }
            if (initialization is not null) await tx.VerifyLeaseAsync(ct);
            var result = manualDay ? tx.RollDailyBaseline(initialization!, first, second, clock.GetUtcNow()) : initialization is null
                ? tx.Reconcile(first, second, clock.GetUtcNow())
                : tx.InitializeBaseline(initialization, first, second, clock.GetUtcNow());
            await Persist(tx);
            return result;
        }, cancellationToken);

    private static LiveBrokerEvidence Freeze(LiveBrokerEvidence evidence)
        => evidence with { Positions = evidence.Positions.ToArray(), Orders = evidence.Orders.ToArray(),
            Fills = evidence.Fills.ToArray(), Quotes = evidence.Quotes.ToArray() };

    private static async Task Persist(ILiveAccountTransaction transaction)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await transaction.CommitAsync(timeout.Token);
    }
}
