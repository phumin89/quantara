using TradingCompany.Domain;

namespace TradingCompany.Application;

/// <summary>Exactly one durable send attempt per intent. Not registered in the paper pipeline.</summary>
public sealed class LiveExecutionService(ILiveTradingStore store, ILiveBroker broker, TimeProvider clock)
{
    public Task SuspendForRecoveryAsync(LiveAccountId accountId, CancellationToken cancellationToken = default)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            tx.SuspendForRecovery(clock.GetUtcNow());
            await tx.CommitAsync(ct);
            return 0;
        }, cancellationToken);
    public Task<LiveOrderRecord> CancelOwnedAsync(LiveAccountId accountId, Guid intentId, CancellationToken cancellationToken = default)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            var order = tx.ReadOrder(intentId) ?? throw new InvalidDataException("Unknown local order.");
            if (order.CancellationRequestedAt is not null)
            {
                if (order.CancellationOutcome is null)
                {
                    tx.RecordCancellation(intentId, BrokerSubmissionState.Unknown, clock.GetUtcNow());
                    await CommitOutcome(tx);
                }
                return tx.ReadOrder(intentId)!;
            }
            if (order.State is LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Rejected) return order;

            tx.RequestCancellation(intentId, clock.GetUtcNow());
            await tx.CommitAsync(ct);
            await tx.VerifyLeaseAsync(ct);
            ct.ThrowIfCancellationRequested();
            BrokerCancellation? response;
            try { response = await broker.CancelAsync(order.Intent.Symbol, order.BrokerId!, order.Intent.Side, ct); }
            catch (Exception) { response = null; }
            var outcome = response is not null && Enum.IsDefined(response.State) ? response.State : BrokerSubmissionState.Unknown;
            tx.RecordCancellation(intentId, outcome, clock.GetUtcNow());
            await CommitOutcome(tx);
            return tx.ReadOrder(intentId)!;
        }, cancellationToken);
    public Task<LiveOrderRecord> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken = default)
        => store.WithAccountLockAsync(intent.AccountId, async (tx, ct) =>
        {
            var existing = tx.ReadOrder(intent.Id);
            if (existing is not null)
            {
                if (existing.Intent != intent) throw new InvalidDataException("Intent identity was reused with different content.");
                if (existing.State == LiveOrderState.Dispatching)
                {
                    tx.RecordTransition(intent.Id, LiveOrderState.Unknown, null, clock.GetUtcNow());
                    await CommitOutcome(tx);
                }
                return tx.ReadOrder(intent.Id)!;
            }

            var order = tx.Reserve(intent, clock.GetUtcNow());
            await tx.CommitAsync(ct);
            if (order.State != LiveOrderState.Reserved) return order;

            try
            {
                var prepared = await broker.PrepareAsync(intent, ct);
                ct.ThrowIfCancellationRequested();
                order = tx.PrepareDispatch(intent.Id, prepared, clock.GetUtcNow());
            }
            catch (Exception)
            {
                // Preparation is contractually read-only. No order has been sent yet.
                tx.RejectBeforeDispatch(intent.Id, "BROKER_PREPARATION_FAILED", clock.GetUtcNow());
                await CommitOutcome(tx);
                return tx.ReadOrder(intent.Id)!;
            }

            // A failed/ambiguous commit must never be followed by an external send.
            await tx.CommitAsync(ct);
            if (order.State != LiveOrderState.Dispatching) return order;
            await tx.VerifyLeaseAsync(ct);
            order = tx.ConfirmDispatch(intent.Id, clock.GetUtcNow());
            if (order.State != LiveOrderState.Dispatching)
            {
                await CommitOutcome(tx);
                return order;
            }
            ct.ThrowIfCancellationRequested();

            BrokerSubmission? submission;
            try { submission = await broker.PlaceAsync(order.PreparedRequest!, ct); }
            catch (Exception) { submission = new(BrokerSubmissionState.Unknown, null, "BROKER_OUTCOME_UNKNOWN"); }
            var next = submission switch
            {
                { State: BrokerSubmissionState.Accepted, BrokerId: var brokerId } when !string.IsNullOrWhiteSpace(brokerId) => LiveOrderState.Acknowledged,
                { State: BrokerSubmissionState.Rejected, BrokerId: null } => LiveOrderState.Rejected,
                _ => LiveOrderState.Unknown
            };
            try { tx.RecordTransition(intent.Id, next, next == LiveOrderState.Acknowledged ? submission!.BrokerId : null, clock.GetUtcNow()); }
            catch (InvalidDataException) { tx.RecordTransition(intent.Id, LiveOrderState.Unknown, null, clock.GetUtcNow()); }
            // Caller cancellation after transmission must not cancel recording an uncertain outcome.
            await CommitOutcome(tx);
            return tx.ReadOrder(intent.Id)!;
        }, cancellationToken);

    private static async Task CommitOutcome(ILiveAccountTransaction transaction)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await transaction.CommitAsync(timeout.Token);
    }
}
