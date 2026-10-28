using Quantara.Portfolio.Application;
using Quantara.Portfolio.Application.Trading;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingReportCompletionChecks
{
    public static async Task Run()
    {
        foreach (var editing in new[] { false, true })
        {
            await BlockedStore(editing, true);
            await BlockedStore(editing, false);
            await BlockedGate(editing);
            await CallerCancellation(editing, false);
            await CallerCancellation(editing, true);
            await TransportCancellation(editing);
        }

        await SharedRetryBudget();
        Console.WriteLine(
            "Trading report completion checks passed: bounded reads, writes and gate waits, one retry budget, caller cancellation and no resends."
        );
    }

    private static async Task BlockedStore(bool editing, bool read)
    {
        var (store, notifier, clock) = Create(editing);
        store.StallRead = read;
        store.StallSave = !read;
        var pending = new TradingReportDelivery(store, notifier, clock).PrepareReports(CancellationToken.None);
        Check(!pending.IsCompleted, "Report completion must reach the simulated blocked I/O.");
        CheckBudget(clock, store);
        clock.FireDeadline();
        await ExpectCancellation(pending);
        Check(
            store.CompletionReads == 1 && store.CompletionSaves == (read ? 0 : 1),
            "A timed-out read or save must not start another completion attempt."
        );
        await RetainedClaimRecovery(store, notifier, clock, editing);
    }

    private static async Task BlockedGate(bool editing)
    {
        var (store, notifier, clock) = Create(editing);
        using var gate = new SemaphoreSlim(1, 1);
        void HoldGate()
        {
            store.Completing = true;
            Check(gate.Wait(0), "Delivery must release the durable claim gate before notifying.");
        }

        notifier.BeforeSend = HoldGate;
        notifier.BeforeEdit = HoldGate;
        var pending = new TradingReportDelivery(store, notifier, clock, gate).PrepareReports(CancellationToken.None);
        Check(!pending.IsCompleted, "Report completion must wait for the held gate.");
        CheckBudget(clock, store);
        clock.FireDeadline();
        await ExpectCancellation(pending);
        Check(
            gate.CurrentCount == 0 && store.CompletionSaves == 0,
            "A timed-out waiter cannot release a gate it never acquired or write the outcome."
        );
        gate.Release();
        await RetainedClaimRecovery(store, notifier, clock, editing);
    }

    private static async Task CallerCancellation(bool editing, bool failure)
    {
        var (store, notifier, clock) = Create(editing);
        using var caller = new CancellationTokenSource();
        void CancelCaller()
        {
            store.Completing = true;
            caller.Cancel();
        }

        notifier.BeforeSend = CancelCaller;
        notifier.BeforeEdit = CancelCaller;
        notifier.FailSend = failure;
        notifier.FailEdit = failure;
        var rejected = false;
        try
        {
            await new TradingReportDelivery(store, notifier, clock).PrepareReports(caller.Token);
        }
        catch (HttpRequestException)
        {
            rejected = true;
        }

        Check(rejected == failure, "An uncertain transport failure must still propagate after outcome persistence.");
        CheckBudget(clock, store);
        var report = store.State.Reports.Single();
        Check(
            caller.IsCancellationRequested
                && store.CompletionTokens.All(token => !token.IsCancellationRequested)
                && report.Status == (failure ? "unknown" : "sent")
                && report.MessageId == (failure && !editing ? null : 42L),
            "Caller cancellation after the durable claim cannot suppress bounded outcome persistence or invent acknowledgement."
        );
        store.Completing = false;
        notifier.BeforeSend = null;
        notifier.BeforeEdit = null;
        notifier.FailSend = false;
        notifier.FailEdit = false;
        await new TradingReportDelivery(store, notifier, clock).PrepareReports(CancellationToken.None);
        CheckDeliveryCount(notifier, editing);
    }

    private static async Task TransportCancellation(bool editing)
    {
        var (store, notifier, clock) = Create(editing);
        using var caller = new CancellationTokenSource();
        var attempts = 0;
        void CancelTransport()
        {
            attempts++;
            store.Completing = true;
            caller.Cancel();
            throw new OperationCanceledException(caller.Token);
        }

        notifier.BeforeSend = CancelTransport;
        notifier.BeforeEdit = CancelTransport;
        await ExpectCancellation(new TradingReportDelivery(store, notifier, clock).PrepareReports(caller.Token));
        CheckBudget(clock, store);
        var report = store.State.Reports.Single();
        Check(
            report.Status == "unknown"
                && report.MessageId == (editing ? 42L : null)
                && store.CompletionTokens.All(token => !token.IsCancellationRequested),
            "A cancelled transport persists uncertainty with the independent completion budget before propagating cancellation."
        );
        store.Completing = false;
        await new TradingReportDelivery(store, notifier, clock).PrepareReports(CancellationToken.None);
        Check(attempts == 1, "Restart cannot repeat a send or unchanged edit after transport cancellation.");
    }

    private static async Task SharedRetryBudget()
    {
        var (store, notifier, clock) = Create(false);
        store.RejectCompletionSaves = true;
        store.BeforeCompletionRead = () =>
        {
            clock.Now += TimeSpan.FromSeconds(6);
            if (store.CompletionReads == 3)
            {
                clock.FireDeadline();
            }
        };
        await ExpectCancellation(
            new TradingReportDelivery(store, notifier, clock).PrepareReports(CancellationToken.None)
        );
        CheckBudget(clock, store);
        Check(
            store.CompletionReads == 3 && store.CompletionSaves == 2,
            "Compare-and-swap conflicts consume one shared completion deadline instead of resetting it per attempt."
        );
        await RetainedClaimRecovery(store, notifier, clock, false);
    }

    private static async Task RetainedClaimRecovery(
        TradingReportCompletionStore store,
        FakeTradingNotifier notifier,
        TradingReportCompletionTime clock,
        bool editing
    )
    {
        var claimed = store.State.Reports.Single();
        Check(
            claimed.Status == (editing ? "editing" : "sending")
                && claimed.ClaimedAt is not null
                && claimed.MessageId == (editing ? 42L : null),
            "A timed-out completion retains the exact durable claim and original message identity."
        );
        store.Completing = false;
        store.StallRead = false;
        store.StallSave = false;
        store.BeforeCompletionRead = null;
        clock.Now += TimeSpan.FromMinutes(3);
        notifier.BeforeSend = null;
        notifier.BeforeEdit = null;
        await new TradingReportDelivery(store, notifier, clock).PrepareReports(CancellationToken.None);
        Check(
            store.State.Reports.Single() == claimed with { Status = "unknown" },
            "Restart recovers a stale claim without replacing its evidence or guessing delivery."
        );
        CheckDeliveryCount(notifier, editing);
    }

    private static void CheckBudget(TradingReportCompletionTime clock, TradingReportCompletionStore store)
    {
        Check(
            clock.TimersCreated == 1 && clock.DeadlineDue == TimeSpan.FromSeconds(15),
            "All report completion attempts share one independent 15-second deadline."
        );
        Check(
            store.CompletionTokens.Count > 0
                && store.CompletionTokens.All(token => token.CanBeCanceled)
                && store.CompletionTokens.Distinct().Count() == 1,
            "Every completion read and write receives the same cancellable budget."
        );
    }

    private static void CheckDeliveryCount(FakeTradingNotifier notifier, bool editing)
    {
        Check(
            notifier.Sends == (editing ? 0 : 1) && notifier.Edits == (editing ? 1 : 0),
            "Restart and uncertain completion cannot repeat the original send or edit."
        );
    }

    private static (
        TradingReportCompletionStore Store,
        FakeTradingNotifier Notifier,
        TradingReportCompletionTime Clock
    ) Create(bool editing)
    {
        var clock = new TradingReportCompletionTime();
        var day = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.FromHours(7));
        var state = TradingStateFactory.Create(day);
        var report = TradingDailyReportBuilder.Build(state.Paper, TradingMode.Paper, "2026-10-16", [], null, clock.Now);
        var store = new TradingReportCompletionStore(
            state with
            {
                Reports = [report with { Status = "prepared", MessageId = editing ? 42L : null }],
            }
        );
        var notifier = new FakeTradingNotifier
        {
            BeforeSend = () => store.Completing = true,
            BeforeEdit = () => store.Completing = true,
        };
        return (store, notifier, clock);
    }

    private static async Task ExpectCancellation(Task pending)
    {
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("A stalled report completion must cancel at its independent deadline.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
