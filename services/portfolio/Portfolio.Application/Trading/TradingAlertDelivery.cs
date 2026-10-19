using System.Globalization;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal sealed class TradingAlertDelivery(
    ITradingStore store,
    ITradingNotifier notifier,
    TimeProvider time,
    SemaphoreSlim? gate,
    ITradingNotificationReceiptStore? receipts
)
{
    private const int recoveryBatchSize = 64;

    internal static readonly TimeSpan SendTimeout = TimeSpan.FromMinutes(2);

    private int recoveryCursor;

    internal static bool IsStale(TradingAlert alert, DateTimeOffset now) =>
        alert.ClaimedAt is null || now - alert.ClaimedAt > TradingAlertDelivery.SendTimeout;

    internal async Task Recover(string? key, CancellationToken cancellationToken)
    {
        var state = await store.Read(cancellationToken);
        var candidates = state
            .Alerts.Where(item =>
                (item.Status == "sending" || item.Status == "unknown" && item.ClaimedAt is not null)
                && (key is null || item.Key == key)
            )
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var cursor = key is null
            ? Interlocked.Add(ref this.recoveryCursor, TradingAlertDelivery.recoveryBatchSize)
                - TradingAlertDelivery.recoveryBatchSize
            : 0;
        var start = (int)((uint)cursor % (uint)candidates.Length);
        for (var index = 0; index < Math.Min(TradingAlertDelivery.recoveryBatchSize, candidates.Length); index++)
        {
            var alert = candidates[(start + index) % candidates.Length];
            var receipt =
                receipts is not null && alert.ClaimedAt is { } claim
                    ? await receipts.Read(alert.Key, claim, cancellationToken)
                    : null;
            if (
                receipt is not null
                && receipt.Key == alert.Key
                && receipt.ClaimedAt == alert.ClaimedAt
                && receipt.MessageId > 0
                && receipt.SentAt >= receipt.ClaimedAt
            )
            {
                await this.Finish(alert, "sent", receipt.MessageId, receipt.SentAt);
            }
            else if (alert.Status == "sending" && TradingAlertDelivery.IsStale(alert, time.GetUtcNow()))
            {
                await this.Finish(alert, "unknown");
            }
        }
    }

    internal async Task<bool> Deliver(string? key, CancellationToken cancellationToken)
    {
        await this.Recover(key, cancellationToken);
        if (!notifier.IsConfigured)
        {
            return false;
        }

        var alert = await this.Claim(key, cancellationToken);
        if (alert is null)
        {
            return false;
        }

        long messageId;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TradingAlertDelivery.SendTimeout);
            messageId = await notifier.Send(alert.Text, timeout.Token);
            if (messageId <= 0)
            {
                throw new InvalidOperationException("Trading alert delivery was not acknowledged.");
            }
        }
        catch (TradingNotificationRejectedException rejection)
        {
            var retry = rejection.Code == 429 && rejection.RetryAfterSeconds is > 0 and <= 3600 && alert.RetryCount < 3;
            await this.Finish(
                alert,
                retry ? "pending" : "rejected",
                retryAt: retry ? time.GetUtcNow().AddSeconds(rejection.RetryAfterSeconds.GetValueOrDefault()) : null,
                retryCount: alert.RetryCount + 1
            );
            throw;
        }
        catch (Exception)
        {
            await this.Finish(alert, "unknown");
            throw;
        }

        var receipt = new TradingNotificationReceipt(
            alert.Key,
            alert.ClaimedAt ?? throw new InvalidOperationException("A durable alert claim is required."),
            messageId,
            time.GetUtcNow()
        );
        await this.Persist(receipt);
        await this.Finish(alert, "sent", receipt.MessageId, receipt.SentAt);
        return true;
    }

    private async Task Persist(TradingNotificationReceipt receipt)
    {
        if (receipts is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await receipts.Save(receipt, timeout.Token);
                return;
            }
            catch (Exception)
            {
                if (attempt == 2 || timeout.IsCancellationRequested)
                {
                    throw new InvalidOperationException(
                        "Notification acknowledgement could not be persisted; its claim is retained without resending."
                    );
                }
            }
        }
    }

    private async Task<TradingAlert?> Claim(string? key, CancellationToken cancellationToken)
    {
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var state = await store.Read(cancellationToken);
                var alert = state.Alerts.FirstOrDefault(item =>
                    item.Status == "pending"
                    && (item.RetryAt is null || item.RetryAt <= time.GetUtcNow())
                    && (key is null || item.Key == key)
                    && (state.Rules.Adaptive is null || item.Kind == "fill")
                );
                if (alert is null)
                {
                    return null;
                }

                var text =
                    time.GetUtcNow() - alert.CreatedAt > TimeSpan.FromMinutes(10)
                    && !alert.Text.StartsWith("🕘 คิวค้างจาก ", StringComparison.Ordinal)
                        ? "🕘 คิวค้างจาก "
                            + alert
                                .CreatedAt.ToOffset(TimeSpan.FromHours(7))
                                .ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)
                            + Environment.NewLine
                            + alert.Text
                        : alert.Text;
                var claimed = alert with { Status = "sending", Text = text, ClaimedAt = time.GetUtcNow() };
                if (
                    await store.Save(
                        state with
                        {
                            Revision = state.Revision + 1,
                            Alerts = state.Alerts.Select(item => item.Key == alert.Key ? claimed : item).ToArray(),
                        },
                        state.Revision,
                        cancellationToken
                    )
                )
                {
                    return claimed;
                }
            }

            return null;
        }
        finally
        {
            gate?.Release();
        }
    }

    private async Task Finish(
        TradingAlert claimed,
        string status,
        long? messageId = null,
        DateTimeOffset? sentAt = null,
        DateTimeOffset? retryAt = null,
        int? retryCount = null
    )
    {
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        if (gate is not null)
        {
            await gate.WaitAsync(completion.Token);
        }

        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var state = await store.Read(completion.Token);
                var current = state.Alerts.FirstOrDefault(alert =>
                    alert.Key == claimed.Key && alert.ClaimedAt == claimed.ClaimedAt && alert.Status == claimed.Status
                );
                if (current is null)
                {
                    return;
                }

                var finished = current with
                {
                    Status = status,
                    MessageId = messageId ?? current.MessageId,
                    SentAt = sentAt ?? current.SentAt,
                    RetryAt = retryAt,
                    RetryCount = retryCount ?? current.RetryCount,
                };
                if (
                    await store.Save(
                        state with
                        {
                            Revision = state.Revision + 1,
                            Alerts = state.Alerts.Select(alert => alert == current ? finished : alert).ToArray(),
                        },
                        state.Revision,
                        completion.Token
                    )
                )
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                "Notification outcome could not be persisted; its claim is retained for recovery."
            );
        }
        finally
        {
            gate?.Release();
        }
    }
}
