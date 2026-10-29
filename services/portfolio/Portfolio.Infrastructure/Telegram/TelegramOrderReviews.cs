using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TelegramOrderReviews(
    TelegramSettings settings,
    IOrderNotificationStore notifications,
    IReadOnlyOrderStore orders,
    ITelegramTransport transport,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<TelegramOrderReviews> logger
)
{
    private static readonly Action<ILogger, Exception?> messageEditFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, "TelegramReviewMessageUnavailable"),
        "Telegram review message update unavailable; review claims remain recorded. Check order status in the app."
    );

    public async Task Notify(CancellationToken cancellationToken)
    {
        foreach (
            var proposal in (await orders.List(cancellationToken)).Where(item =>
                item.State == OrderState.Pending && item.ExpiresAt > time.GetUtcNow()
            )
        )
        {
            var notification = await notifications.Reserve(
                proposal.Id,
                settings.UserId,
                settings.ChatId,
                cancellationToken
            );
            if (notification is null)
            {
                continue;
            }
            // Reservation precedes sending. Uncertain delivery is never resent automatically.
            var messageId = await transport.Send(proposal, notification.Token, cancellationToken);
            await notifications.Sent(notification.Token, messageId, cancellationToken);
        }
    }

    public async Task Process(TelegramClick click, CancellationToken cancellationToken)
    {
        if (
            click.UserId != settings.UserId
            || click.ChatId != settings.ChatId
            || click.Data.Length != 34
            || click.Data[1] != ':'
            || click.Data[0] is not ('a' or 'r')
            || !Guid.TryParseExact(click.Data[2..], "N", out _)
        )
        {
            return;
        }

        var notification = await notifications.Find(click.Data[2..], cancellationToken);
        if (
            notification is null
            || notification.UserId != click.UserId
            || notification.ChatId != click.ChatId
            || notification.MessageId != click.MessageId
        )
        {
            return;
        }

        var proposal = await orders.Find(notification.OrderId, cancellationToken);
        if (proposal is null)
        {
            return;
        }

        var status = "Already reviewed. Check the app for the current order status.";
        if (
            notification.State == OrderNotificationState.Sent
            && await notifications.ClaimReview(
                notification.Token,
                click.Data[0] == 'a' ? "approve" : "reject",
                cancellationToken
            )
        )
        {
            if (time.GetUtcNow() >= proposal.ExpiresAt)
            {
                status = "⌛ Expired · No order submitted";
            }
            else
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope
                        .ServiceProvider.GetRequiredService<IHumanOrderReview>()
                        .Review(proposal.Id, click.Data[0] == 'a', cancellationToken);
                    status =
                        result.Status == "submitted"
                            ? "✅ Submitted to Bitkub · Fill not confirmed"
                            : "Order status · " + result.Status;
                }
                catch (OrderValidationException error)
                {
                    status = "Not submitted · " + error.Message;
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    status = "Review interrupted · Check the app/Bitkub. Do not resend an uncertain order.";
                }
            }
        }
        // Re-read after review: message rendering and later replay never trigger execution.
        proposal = await orders.Find(proposal.Id, cancellationToken) ?? proposal;
        if (notification.State != OrderNotificationState.Sent)
        {
            status =
                proposal.State == OrderState.Pending
                    ? "Review already recorded · Use the app to check this proposal"
                    : "Order status · " + proposal.State;
        }

        var feedback = "บันทึกแล้ว ดูผลในข้อความนี้";
        try
        {
            await transport.Edit(click.MessageId, proposal, status, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            TelegramOrderReviews.messageEditFailed(logger, null);
            feedback = "แก้ข้อความไม่สำเร็จ กรุณาตรวจสอบผลการพิจารณาในแอป";
        }

        try
        {
            using var popupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            popupTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            await transport.Answer(click.CallbackId, feedback, popupTimeout.Token);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Expired callback popups cannot roll back or repeat a submitted order.
        }
    }
}
