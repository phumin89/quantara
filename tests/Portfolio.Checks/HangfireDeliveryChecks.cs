using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;
using Quantara.Portfolio.Infrastructure.Trading.Jobs;

namespace Portfolio.Checks;

public static class HangfireDeliveryChecks
{
    public static async Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var state = TradingStateFactory.Create(now) with
        {
            Alerts =
            [
                new TradingAlert("first", "fill", "first alert", now),
                new TradingAlert("second", "fill", "second alert", now),
                new TradingAlert("sending", "fill", "already sending", now) with
                {
                    Status = "sending",
                },
                new TradingAlert("unknown", "fill", "already unknown", now) with
                {
                    Status = "unknown",
                },
            ],
        };
        var store = new FakeTradingDeliveryStore(state);
        var notifier = new FakeTradingNotifier();
        var delivery = new Quantara.Portfolio.Application.TradingReportDelivery(
            store,
            notifier,
            new OrderCheckTime { Now = now }
        );
        var job = new TradingNotificationJob(
            new AccountContext(new string('a', 64)),
            delivery,
            new TradingJobOptions(new string('a', 64), "destination", true)
        );
        var disabledJob = new TradingNotificationJob(
            new AccountContext(new string('a', 64)),
            delivery,
            new TradingJobOptions(new string('a', 64), "destination", false)
        );
        await ExpectFailure(
            () => disabledJob.Deliver(new string('a', 64), "destination", "first", CancellationToken.None),
            "disabled options do not deliver"
        );

        await job.Deliver(new string('a', 64), "destination", "first", CancellationToken.None);
        Check(
            notifier.Sends == 1 && store.State.Alerts.Single(item => item.Key == "first").Status == "sent",
            "bound alert sends exactly once"
        );
        await job.Deliver(new string('a', 64), "destination", "first", CancellationToken.None);
        Check(notifier.Sends == 1, "repeated alert job does not resend");
        await job.Deliver(new string('a', 64), "destination", "second", CancellationToken.None);
        Check(notifier.Sends == 2 && notifier.LastText == "second alert", "job selects the exact alert key");

        await ExpectFailure(
            () => job.Deliver(new string('b', 64), "destination", "second", CancellationToken.None),
            "account mismatch is rejected"
        );
        await ExpectFailure(
            () => job.Deliver(new string('a', 64), "other", "second", CancellationToken.None),
            "destination mismatch is rejected"
        );
        Check(notifier.Sends == 2, "sending and unknown alerts are never replayed");
        Check(store.State.Alerts.Single(item => item.Key == "first").MessageId == 42, "message ID is retained");
        Check(store.State.Alerts.Single(item => item.Key == "first").SentAt is not null, "sent timestamp is retained");

        var notificationMethod =
            typeof(TradingNotificationJob).GetMethod(nameof(TradingNotificationJob.Deliver))
            ?? throw new InvalidOperationException("Notification method is missing.");
        var attributes = notificationMethod.GetCustomAttributes(true);
        Check(attributes.OfType<QueueAttribute>().Single().Queue == "telegram", "notification queue is telegram");
        Check(attributes.OfType<AutomaticRetryAttribute>().Single().Attempts == 0, "notification retries are disabled");
        var reportMethod =
            typeof(TradingReportJob).GetMethod(nameof(TradingReportJob.Prepare))
            ?? throw new InvalidOperationException("Report method is missing.");
        var reportAttributes = reportMethod.GetCustomAttributes(true);
        Check(reportAttributes.OfType<QueueAttribute>().Single().Queue == "maintenance", "report queue is maintenance");
        Check(reportAttributes.OfType<AutomaticRetryAttribute>().Single().Attempts == 0, "report retries are disabled");
        await RetryAndFailureChecks(now);
        LegacyHashCheck();
        await TradingNotifierTransportChecks();
        Console.WriteLine("Hangfire delivery checks passed.");
    }

    private static async Task RetryAndFailureChecks(DateTimeOffset now)
    {
        var alert = new TradingAlert("retry", "fill", "retry alert", now);
        var clock = new OrderCheckTime { Now = now };
        var store = new FakeTradingDeliveryStore(TradingStateFactory.Create(now) with { Alerts = [alert] });
        var notifier = new HangfireDeliveryNotifier
        {
            SendBehavior = () => Task.FromException<long>(new TradingNotificationRejectedException(429, 10)),
        };
        var job = new TradingNotificationJob(
            new AccountContext(new string('a', 64)),
            new TradingReportDelivery(store, notifier, clock),
            new TradingJobOptions(new string('a', 64), "destination", true)
        );
        await ExpectFailure(
            () => job.Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None),
            "429 should be surfaced"
        );
        Check(
            store.State.Alerts.Single().Status == "pending"
                && store.State.Alerts.Single().RetryCount == 1
                && store.State.Alerts.Single().RetryAt == now.AddSeconds(10),
            "429 retry metadata is persisted"
        );
        await job.Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None);
        Check(notifier.Sends == 1, "cooldown prevents immediate resend");
        clock.Now = now.AddSeconds(11);
        notifier = new HangfireDeliveryNotifier { SendBehavior = () => Task.FromResult(42L) };
        await new TradingNotificationJob(
            new AccountContext(new string('a', 64)),
            new TradingReportDelivery(store, notifier, clock),
            new TradingJobOptions(new string('a', 64), "destination", true)
        ).Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None);
        Check(notifier.Sends == 1 && store.State.Alerts.Single().Status == "sent", "retry sends once after cooldown");

        store = new FakeTradingDeliveryStore(TradingStateFactory.Create(now) with { Alerts = [alert] });
        notifier = new HangfireDeliveryNotifier
        {
            SendBehavior = () => Task.FromException<long>(new TradingNotificationRejectedException(403)),
        };
        job = new TradingNotificationJob(
            new AccountContext(new string('a', 64)),
            new TradingReportDelivery(store, notifier, clock),
            new TradingJobOptions(new string('a', 64), "destination", true)
        );
        await ExpectFailure(
            () => job.Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None),
            "403 should be surfaced"
        );
        Check(store.State.Alerts.Single().Status == "rejected", "permanent rejection is retained");
        await job.Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None);
        Check(notifier.Sends == 1, "permanent rejection is not retried");

        store = new FakeTradingDeliveryStore(TradingStateFactory.Create(now) with { Alerts = [alert] });
        notifier = new HangfireDeliveryNotifier
        {
            SendBehavior = () => Task.FromException<long>(new TimeoutException()),
        };
        job = new TradingNotificationJob(
            new AccountContext(new string('a', 64)),
            new TradingReportDelivery(store, notifier, clock),
            new TradingJobOptions(new string('a', 64), "destination", true)
        );
        await ExpectFailure(
            () => job.Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None),
            "timeout should be surfaced"
        );
        Check(store.State.Alerts.Single().Status == "unknown", "timeout is unknown");
        await job.Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None);
        Check(notifier.Sends == 1, "unknown notification is not retried");

        store = new FakeTradingDeliveryStore(TradingStateFactory.Create(now) with { Alerts = [alert] })
        {
            RejectSave = candidate => candidate.Alerts.Any(item => item.Status == "sent"),
        };
        notifier = new HangfireDeliveryNotifier();
        job = new TradingNotificationJob(
            new AccountContext(new string('a', 64)),
            new TradingReportDelivery(store, notifier, clock),
            new TradingJobOptions(new string('a', 64), "destination", true)
        );
        await ExpectFailure(
            () => job.Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None),
            "Unpersisted delivery outcome must remain visible as a failed job."
        );
        await job.Deliver(new string('a', 64), "destination", alert.Key, CancellationToken.None);
        Check(
            notifier.Sends == 1 && store.State.Alerts.Single().Status == "sending",
            "lost finish acknowledgement never resends"
        );
    }

    private static void LegacyHashCheck()
    {
        var rules = new TradingRuleSet { Version = "adaptive-intraday-v2", ExitPolicy = null };
        var frozen = JsonSerializer.Serialize(
            new
            {
                rules.Version,
                rules.Symbols,
                rules.AllocatedThb,
                rules.PerOrderThb,
                rules.DailyLossThb,
                rules.MaxSpreadBps,
                rules.SlippageBps,
                rules.TakeProfitBps,
                rules.StopLossBps,
                rules.TrailingStopBps,
                rules.FeeBps,
                rules.VolumeRatio,
                rules.MaxPositions,
                rules.MaxDataAgeSeconds,
                rules.TimeExitMinutes,
                rules.CooldownMinutes,
                rules.OrderLifetimeMinutes,
                rules.Adaptive,
            }
        );
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(frozen))).ToLowerInvariant();
        Check(TradingRuleHash.Compute(rules) == expected, "v2 hash preserves frozen legacy serialization");
    }

    private static async Task TradingNotifierTransportChecks()
    {
        using var okHandler = new FakeTradingHttpHandler();
        using var okHttp = new HttpClient(okHandler) { BaseAddress = new Uri("https://telegram.test") };
        var ok = new TradingNotifier(okHttp, new TelegramSettings("token", 1, 2));
        Check(
            await ok.Send("hello", CancellationToken.None) == 42 && okHandler.Calls == 1,
            "Telegram success returns the message ID."
        );

        var retryHandler = new FakeTradingHttpHandler
        {
            StatusCode = (HttpStatusCode)429,
            ResponseJson = "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":10}}",
        };
        using var retryHttp = new HttpClient(retryHandler) { BaseAddress = new Uri("https://telegram.test") };
        var retry = new TradingNotifier(retryHttp, new TelegramSettings("token", 1, 2));
        try
        {
            await retry.Send("hello", CancellationToken.None);
            throw new InvalidOperationException("429 should reject.");
        }
        catch (TradingNotificationRejectedException ex)
        {
            Check(ex.Code == 429 && ex.RetryAfterSeconds == 10, "Telegram 429 preserves retry_after.");
        }

        var forbiddenHandler = new FakeTradingHttpHandler
        {
            StatusCode = HttpStatusCode.Forbidden,
            ResponseJson = "{\"ok\":false,\"error_code\":403}",
        };
        using var forbiddenHttp = new HttpClient(forbiddenHandler) { BaseAddress = new Uri("https://telegram.test") };
        var forbidden = new TradingNotifier(forbiddenHttp, new TelegramSettings("token", 1, 2));
        try
        {
            await forbidden.Send("hello", CancellationToken.None);
            throw new InvalidOperationException("403 should reject.");
        }
        catch (TradingNotificationRejectedException ex)
        {
            Check(ex.Code == 403, "Telegram 403 is explicit rejection.");
        }

        using var timeoutHandler = new FakeTradingHttpHandler { ThrowTimeout = true };
        using var timeoutHttp = new HttpClient(timeoutHandler) { BaseAddress = new Uri("https://telegram.test") };
        var timeout = new TradingNotifier(timeoutHttp, new TelegramSettings("token", 1, 2));
        try
        {
            await timeout.Send("hello", CancellationToken.None);
            throw new InvalidOperationException("Timeout should fail.");
        }
        catch (InvalidOperationException ex)
        {
            Check(
                ex.Message.Contains("uncertain", StringComparison.OrdinalIgnoreCase)
                    && !ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase),
                "Telegram timeout is wrapped as uncertain."
            );
        }
    }

    private static async Task ExpectFailure(Func<Task> operation, string name)
    {
        try
        {
            await operation();
        }
        catch (Exception)
        {
            return;
        }

        throw new InvalidOperationException(name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
