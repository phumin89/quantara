using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingNotificationRecoveryChecks
{
    public static async Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        await RecoverLegacyClaims(now);
        await RecoverPersistedAcknowledgement(now);
        await ReceiptWriteFailures(now);
        await ConcurrentJobs(now);
        await ExactClaimRecovery(now);
        await FailedSendSafety(now);
        await BoundedRecovery(now);
        Console.WriteLine(
            "Trading notification recovery checks passed: receipts, restart, exact claims, failures and concurrent jobs."
        );
    }

    private static async Task RecoverLegacyClaims(DateTimeOffset now)
    {
        var legacy = new TradingAlert("legacy", "fill", "ambiguous legacy send", now.AddHours(-1), "sending");
        var fresh = new TradingAlert("fresh", "fill", "in-flight send", now, "sending") { ClaimedAt = now };
        var stale = fresh with { Key = "stale", ClaimedAt = now.AddMinutes(-3) };
        var boundary = fresh with { Key = "boundary", ClaimedAt = now.AddMinutes(-2) };
        var unknown = fresh with { Key = "unknown", Status = "unknown", ClaimedAt = now.AddMinutes(-3) };
        var store = new FakeTradingDeliveryStore(
            TradingStateFactory.Create(now) with
            {
                Alerts = [legacy, fresh, stale, boundary, unknown],
            }
        );
        var notifier = new FakeTradingNotifier();
        var clock = new OrderCheckTime { Now = now };
        var delivery = new TradingReportDelivery(store, notifier, clock);
        await delivery.PrepareReports(CancellationToken.None);
        Check(
            store.State.Alerts.Single(alert => alert.Key == legacy.Key).Status == "unknown"
                && store.State.Alerts.Single(alert => alert.Key == fresh.Key).Status == "sending"
                && store.State.Alerts.Single(alert => alert.Key == stale.Key).Status == "unknown"
                && store.State.Alerts.Single(alert => alert.Key == boundary.Key).Status == "sending"
                && store.State.Alerts.Single(alert => alert.Key == unknown.Key).Status == "unknown"
                && notifier.Sends == 0,
            "Maintenance must durably recover legacy sends as unknown while retaining fresh claims without resending."
        );

        var projection = new TradingOverviewProjection(
            new AccountContext(new string('a', 64)),
            new TradingControlOptions("owner"),
            clock,
            notifications: new TradingNotificationDiagnostics(true)
        );
        using var view = JsonDocument.Parse(projection.Serialize(store.State with { Alerts = [legacy, fresh] }));
        var notifications = view.RootElement.GetProperty("notifications");
        Check(
            notifications.GetProperty("sending").GetInt32() == 1
                && notifications.GetProperty("unknown").GetInt32() == 1
                && notifications.GetProperty("state").GetString() == "needsReview",
            "Notification counts must classify each stale claim once, separate from fresh sends."
        );
        clock.Now = now.AddMinutes(3);
        notifier.IsConfigured = false;
        await delivery.DeliverAlert(fresh.Key, CancellationToken.None);
        Check(
            store.State.Alerts.Single(alert => alert.Key == fresh.Key).Status == "unknown",
            "Targeted delivery repairs stale claims even with notification transport disabled."
        );
    }

    private static async Task RecoverPersistedAcknowledgement(DateTimeOffset now)
    {
        var alert = new TradingAlert("acknowledged", "fill", "acknowledged send", now);
        var store = Store(now, alert);
        var receipts = new FakeTradingNotificationReceiptStore();
        var notifier = new FakeTradingNotifier();
        var clock = new OrderCheckTime { Now = now };
        store.RejectSave = state => state.Alerts.Any(item => item.Status == "sent");
        var delivery = new TradingReportDelivery(store, notifier, clock, receipts: receipts);
        await ExpectFailure(() => delivery.DeliverAlert(alert.Key, CancellationToken.None));
        var receipt = receipts.Receipts.Single();
        Check(
            notifier.Sends == 1 && receipt.MessageId == 42 && store.State.Alerts.Single().Status == "sending",
            "A positive Telegram acknowledgement is durably retained before an aggregate finish failure."
        );

        store.RejectSave = null;
        clock.Now = now.AddMinutes(5);
        var restartedNotifier = new FakeTradingNotifier { IsConfigured = false };
        var restarted = new TradingReportDelivery(store, restartedNotifier, clock, receipts: receipts);
        await restarted.PrepareReports(CancellationToken.None);
        await restarted.DeliverAlert(alert.Key, CancellationToken.None);
        var recovered = store.State.Alerts.Single();
        Check(
            restartedNotifier.Sends == 0
                && recovered.Status == "sent"
                && recovered.MessageId == 42
                && recovered.SentAt == now
                && recovered.ClaimedAt == now,
            "Restart recovers the exact acknowledged claim and original timestamp without a network send."
        );

        store.State = store.State with
        {
            Alerts = [recovered with { Status = "unknown", MessageId = null, SentAt = null }],
        };
        await restarted.PrepareReports(CancellationToken.None);
        Check(
            store.State.Alerts.Single().Status == "sent" && restartedNotifier.Sends == 0,
            "An exact retained acknowledgement can resolve unknown without a resend."
        );
    }

    private static async Task ReceiptWriteFailures(DateTimeOffset now)
    {
        foreach (var persistThenThrow in new[] { false, true })
        {
            var alert = new TradingAlert("receipt-retry", "fill", "receipt retry", now);
            var store = Store(now, alert);
            var receipts = new FakeTradingNotificationReceiptStore
            {
                FailWrites = persistThenThrow ? 0 : 2,
                FailAfterSave = persistThenThrow ? 1 : 0,
            };
            var notifier = new FakeTradingNotifier();
            await new TradingReportDelivery(
                store,
                notifier,
                new OrderCheckTime { Now = now },
                receipts: receipts
            ).DeliverAlert(alert.Key, CancellationToken.None);
            Check(
                notifier.Sends == 1
                    && store.State.Alerts.Single().Status == "sent"
                    && receipts.Receipts.Count == 1
                    && receipts.SaveAttempts == (persistThenThrow ? 2 : 3),
                "Only idempotent receipt storage retries after a positive send acknowledgement."
            );
        }

        var failed = new TradingAlert("receipt-outage", "fill", "receipt outage", now);
        var failedStore = Store(now, failed);
        var failedReceipts = new FakeTradingNotificationReceiptStore { FailWrites = 10 };
        var failedNotifier = new FakeTradingNotifier();
        var clock = new OrderCheckTime { Now = now };
        var delivery = new TradingReportDelivery(failedStore, failedNotifier, clock, receipts: failedReceipts);
        await ExpectFailure(() => delivery.DeliverAlert(failed.Key, CancellationToken.None));
        Check(
            failedNotifier.Sends == 1
                && failedReceipts.SaveAttempts == 3
                && failedStore.State.Alerts.Single().Status == "sending",
            "Exhausted receipt writes retain the durable claim and fail visibly without a second send."
        );
        clock.Now = now.AddMinutes(3);
        await delivery.DeliverAlert(failed.Key, CancellationToken.None);
        Check(
            failedNotifier.Sends == 1 && failedStore.State.Alerts.Single().Status == "unknown",
            "Without retained proof, a stale acknowledged send remains unknown and never becomes pending."
        );
    }

    private static async Task ConcurrentJobs(DateTimeOffset now)
    {
        var alert = new TradingAlert("concurrent", "fill", "concurrent send", now);
        var store = Store(now, alert);
        var receipts = new FakeTradingNotificationReceiptStore();
        var sent = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifier = new HangfireDeliveryNotifier { SendBehavior = () => sent.Task };
        using var gate = new SemaphoreSlim(1, 1);
        var clock = new OrderCheckTime { Now = now };
        var delivery = new TradingReportDelivery(store, notifier, clock, gate, receipts);
        var restarted = new TradingReportDelivery(store, notifier, clock, gate, receipts);
        var first = delivery.DeliverAlert(alert.Key, CancellationToken.None);
        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => restarted.DeliverAlert(alert.Key, CancellationToken.None))
        );
        Check(
            notifier.Sends == 1 && store.State.Alerts.Single().Status == "sending",
            "Concurrent duplicate jobs preserve one durable sending claim."
        );
        sent.SetResult(42);
        await first;
        await restarted.DeliverAlert(alert.Key, CancellationToken.None);
        Check(
            notifier.Sends == 1 && receipts.Receipts.Count == 1 && store.State.Alerts.Single().Status == "sent",
            "Concurrent and repeated jobs produce at most one send and one retained acknowledgement."
        );
    }

    private static async Task ExactClaimRecovery(DateTimeOffset now)
    {
        var alert = new TradingAlert("exact", "fill", "exact claim", now, "sending") { ClaimedAt = now };
        var store = Store(now, alert);
        var receipts = new FakeTradingNotificationReceiptStore();
        await receipts.Save(new(alert.Key, now.AddSeconds(-1), 42, now), CancellationToken.None);
        var notifier = new FakeTradingNotifier();
        var clock = new OrderCheckTime { Now = now };
        var delivery = new TradingReportDelivery(store, notifier, clock, receipts: receipts);
        await delivery.PrepareReports(CancellationToken.None);
        Check(store.State.Alerts.Single().Status == "sending", "A prior claim receipt cannot finish a new claim.");
        receipts.ReadOverride = new("other-alert", now, 42, now);
        await delivery.PrepareReports(CancellationToken.None);
        Check(store.State.Alerts.Single().Status == "sending", "A wrong alert receipt cannot finish a claim.");
        receipts.ReadOverride = new(alert.Key, now.AddSeconds(-1), 42, now);
        clock.Now = now.AddMinutes(3);
        await delivery.PrepareReports(CancellationToken.None);
        Check(
            store.State.Alerts.Single().Status == "unknown" && notifier.Sends == 0,
            "Mismatched proof leaves an expired claim unknown without resending."
        );

        var replacement = alert with { Key = "replacement", Status = "pending", ClaimedAt = null };
        store = Store(now, replacement);
        notifier = new FakeTradingNotifier();
        notifier.BeforeSend = () =>
            store.State = store.State with
            {
                Revision = store.State.Revision + 1,
                Alerts = [store.State.Alerts.Single() with { ClaimedAt = now.AddSeconds(1) }],
            };
        await new TradingReportDelivery(
            store,
            notifier,
            new OrderCheckTime { Now = now },
            receipts: receipts
        ).DeliverAlert(replacement.Key, CancellationToken.None);
        Check(
            store.State.Alerts.Single().Status == "sending"
                && store.State.Alerts.Single().ClaimedAt == now.AddSeconds(1),
            "Finishing an older send cannot overwrite a concurrently replaced claim."
        );
    }

    private static async Task FailedSendSafety(DateTimeOffset now)
    {
        foreach (
            var failure in new Exception[]
            {
                new TradingNotificationRejectedException(403),
                new TradingNotificationRejectedException(429, 10),
                new TimeoutException("Uncertain send fixture."),
            }
        )
        {
            var alert = new TradingAlert("failed-send", "fill", "failed send", now);
            var store = Store(now, alert);
            var receipts = new FakeTradingNotificationReceiptStore();
            var notifier = new HangfireDeliveryNotifier { SendBehavior = () => Task.FromException<long>(failure) };
            var delivery = new TradingReportDelivery(
                store,
                notifier,
                new OrderCheckTime { Now = now },
                receipts: receipts
            );
            await ExpectFailure(() => delivery.DeliverAlert(alert.Key, CancellationToken.None));
            var expected = failure is TradingNotificationRejectedException rejection
                ? rejection.Code == 429
                    ? "pending"
                    : "rejected"
                : "unknown";
            await delivery.DeliverAlert(alert.Key, CancellationToken.None);
            Check(
                store.State.Alerts.Single().Status == expected && receipts.Receipts.Count == 0 && notifier.Sends == 1,
                "Rejection cooldowns and uncertain outcomes retain their safety semantics without fabricated receipts."
            );
        }
    }

    private static async Task BoundedRecovery(DateTimeOffset now)
    {
        var unknowns = Enumerable
            .Range(0, 130)
            .Select(index => new TradingAlert(
                "retained-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "fill",
                "retained unknown",
                now.AddHours(-1),
                "unknown"
            )
            {
                ClaimedAt = now.AddHours(-1),
            })
            .ToArray();
        var legacy = new TradingAlert("last-legacy", "fill", "legacy claim", now.AddHours(-1), "sending");
        var store = new FakeTradingDeliveryStore(
            TradingStateFactory.Create(now) with
            {
                Alerts = unknowns.Append(legacy).ToArray(),
            }
        );
        var receipts = new FakeTradingNotificationReceiptStore();
        var notifier = new FakeTradingNotifier { IsConfigured = false };
        var delivery = new TradingReportDelivery(store, notifier, new OrderCheckTime { Now = now }, receipts: receipts);
        for (var pass = 0; pass < 4; pass++)
        {
            var before = receipts.Reads;
            await delivery.PrepareReports(CancellationToken.None);
            Check(receipts.Reads - before <= 64, "Maintenance recovery performs at most 64 receipt reads per pass.");
        }

        Check(
            receipts.ReadKeys.Count == unknowns.Length
                && store.State.Alerts.All(alert => alert.Status == "unknown")
                && notifier.Sends == 0,
            "A rotating recovery cursor eventually inspects all retained claims and repairs legacy sends without replay."
        );
    }

    private static FakeTradingDeliveryStore Store(DateTimeOffset now, TradingAlert alert) =>
        new(TradingStateFactory.Create(now) with { Alerts = [alert] });

    private static async Task ExpectFailure(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception)
        {
            return;
        }

        throw new InvalidOperationException("A failed notification persistence or transport operation was expected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
