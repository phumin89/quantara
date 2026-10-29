using Microsoft.Extensions.DependencyInjection;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TelegramReviewChecks
{
    public static async Task Run()
    {
        await TelegramReviewChecks.CheckPollingProgress();
        await TelegramReviewChecks.CheckPollingProgress("lookup");
        await TelegramReviewChecks.CheckPollingProgress("orderLookup");
        await TelegramReviewChecks.CheckPollingProgress("claim");
        await TelegramReviewChecks.CheckPollingProgress("claimAcknowledgement");
        await TelegramReviewChecks.CheckPollingProgress("afterReview");
        await TelegramReviewChecks.CheckPollingProgress("advance");
        await TelegramReviewChecks.CheckPollingProgress("advanceAcknowledgement");
        using var telegramHttp = new TelegramHttpHandler
        {
            Response =
                "{\"ok\":true,\"result\":[{\"update_id\":7,\"callback_query\":{\"id\":\"old\",\"from\":{\"id\":123},\"message\":{\"message_id\":42,\"chat\":{\"id\":123,\"type\":\"private\"}}}},{\"update_id\":8}]}",
        };
        using var telegramClient = new HttpClient(telegramHttp) { BaseAddress = new Uri("https://telegram.invalid/") };
        var telegramTransport = new TelegramTransport(telegramClient, new("123:fixture-token", 123, 123));
        var malformedClicks = await telegramTransport.Poll(0, CancellationToken.None);
        TelegramReviewChecks.Check(
            telegramHttp.RequestAddress?.Scheme == "https" && telegramHttp.RequestAddress.Host == "telegram.invalid",
            "Bot token colon must not become a URI scheme; stay on the configured HTTPS host."
        );
        TelegramReviewChecks.Check(
            malformedClicks.Count == 2 && malformedClicks.All(item => item.Data.Length == 0),
            "Malformed or unrelated updates cannot block the queue or authorize orders."
        );
        TelegramReviewChecks.Check(
            telegramHttp.RequestBody?.Contains("callback_query", StringComparison.Ordinal) == true,
            "Enable Telegram callback updates explicitly."
        );
        foreach (
            var scenario in new[]
            {
                "approve",
                "reject",
                "wrongUser",
                "wrongChat",
                "wrongMessage",
                "unknownToken",
                "expired",
                "unknownSubmission",
                "editFailure",
                "unknownEditFailure",
                "editCancellation",
                "answerCancellation",
                "editTimeout",
                "answerTimeout",
                "deliveryFailure",
                "mockClick",
                "sell",
                "concurrent",
            }
        )
        {
            var orders = new FakeOrderStore();
            var broker = new FakeOrderBroker();
            var time = new OrderCheckTime();
            var draft = await new CreateOrderHandler(orders, broker, time).Handle(
                new(
                    "BTC",
                    scenario == "sell" ? TradeSide.Sell : TradeSide.Buy,
                    scenario == "sell" ? "0.2" : "100",
                    "100",
                    "Signal <b>must not inject HTML</b>"
                ),
                CancellationToken.None
            );
            var notifications = new FakeOrderNotificationStore();
            var transport = new FakeTelegramTransport { FailSend = scenario == "deliveryFailure" };
            var logger = new TelegramReviewLogger<TelegramOrderReviews>();
            using var services = new ServiceCollection()
                .AddSingleton<IHumanOrderReview>(new FakeHumanOrderReview(orders, broker, time))
                .BuildServiceProvider();
            var reviews = new TelegramOrderReviews(
                new("fake", 123, 123),
                notifications,
                orders,
                transport,
                services.GetRequiredService<IServiceScopeFactory>(),
                time,
                logger
            );
            try
            {
                await reviews.Notify(CancellationToken.None);
            }
            catch (HttpRequestException) when (scenario == "deliveryFailure") { }

            await reviews.Notify(CancellationToken.None);
            TelegramReviewChecks.Check(
                transport.Sends == 1 && broker.Submissions == 0,
                "Notify neither resends nor trades."
            );
            if (scenario == "deliveryFailure")
            {
                continue;
            }

            var click = new TelegramClick(1, "callback", 123, 123, 42, "a:" + transport.Token);
            click = scenario switch
            {
                "reject" => click with { Data = "r:" + transport.Token },
                "wrongUser" => click with { UserId = 456 },
                "wrongChat" => click with { ChatId = 456 },
                "wrongMessage" => click with { MessageId = 99 },
                "unknownToken" => click with { Data = "a:" + Guid.NewGuid().ToString("N") },
                "mockClick" => click with { Data = "mock:v2:approve" },
                _ => click,
            };
            if (scenario == "expired")
            {
                time.Now = draft.ExpiresAt;
            }

            broker.FailSubmit = scenario is "unknownSubmission" or "unknownEditFailure";
            transport.FailEdit = scenario is "editFailure" or "unknownEditFailure";
            transport.TimeoutEdit = scenario == "editTimeout";
            transport.TimeoutAnswer = scenario == "answerTimeout";
            using var cancellation = new CancellationTokenSource();
            transport.Editing = scenario == "editCancellation" ? cancellation.Cancel : null;
            transport.Answering = scenario == "answerCancellation" ? cancellation.Cancel : null;
            var canceled = false;
            try
            {
                if (scenario == "concurrent")
                {
                    broker.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var first = reviews.Process(click, CancellationToken.None);
                    var second = reviews.Process(click, CancellationToken.None);
                    broker.Release.SetResult();
                    await Task.WhenAll(first, second);
                }
                else
                {
                    await reviews.Process(click, cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                canceled = true;
            }

            TelegramReviewChecks.Check(
                canceled == (scenario is "editCancellation" or "answerCancellation"),
                "Caller cancellation during Edit or Answer propagates without being treated as a display failure."
            );
            TelegramReviewChecks.Check(
                !canceled || transport.Answers.Count == 0,
                "Cancellation must not claim callback feedback was delivered."
            );

            transport.FailEdit = false;
            transport.TimeoutEdit = false;
            transport.TimeoutAnswer = false;
            transport.Editing = null;
            transport.Answering = null;
            await reviews.Process(click, CancellationToken.None);
            var submitted =
                scenario
                is "approve"
                    or "unknownSubmission"
                    or "editFailure"
                    or "unknownEditFailure"
                    or "editCancellation"
                    or "answerCancellation"
                    or "editTimeout"
                    or "answerTimeout"
                    or "sell"
                    or "concurrent";
            TelegramReviewChecks.Check(
                broker.Submissions == (submitted ? 1 : 0),
                "Only the first authenticated unexpired approve submits."
            );
            var result =
                await orders.Find(draft.Id, CancellationToken.None)
                ?? throw new InvalidOperationException("Missing fixture");
            TelegramReviewChecks.Check(
                scenario is not ("unknownSubmission" or "unknownEditFailure") || result.State == OrderState.Unknown,
                "Uncertain submission retains Unknown."
            );
            TelegramReviewChecks.Check(
                scenario != "reject" || result.State == OrderState.Rejected,
                "Reject never submits."
            );
            TelegramReviewChecks.Check(
                !submitted || transport.LastEditedMessage == 42,
                "Edit the bound message, including replay after failed display."
            );
            TelegramReviewChecks.Check(
                TelegramTransport.Text(result, "Pending").Contains("&lt;b&gt;", StringComparison.Ordinal),
                "Escape model text before HTML rendering."
            );
            TelegramReviewChecks.Check(
                TelegramTransport
                    .Text(result with { Amount = 0.000000000000000001m }, "Pending")
                    .Contains("0.000000000000000001", StringComparison.Ordinal),
                "Display preserves the exact immutable decimal, without rounding."
            );
        }

        Console.WriteLine(
            "Telegram reviews: owner/chat/message binding, expiry, immutable replay, unknown/delivery failures and real-handler fake-broker checks passed."
        );
    }

    private static async Task CheckPollingProgress(string? storeFailure = null)
    {
        var orders = new FakeOrderStore();
        var broker = new FakeOrderBroker();
        var time = new OrderCheckTime();
        var create = new CreateOrderHandler(orders, broker, time);
        var first = await create.Handle(
            new("BTC", TradeSide.Buy, "100", "100", "First fixture"),
            CancellationToken.None
        );
        var second = await create.Handle(
            new("BTC", TradeSide.Buy, "100", "100", "Second fixture"),
            CancellationToken.None
        );
        var notifications = new FakeOrderNotificationStore();
        var firstNotification =
            await notifications.Reserve(first.Id, 123, 123, CancellationToken.None)
            ?? throw new InvalidOperationException("Missing first notification fixture");
        var secondNotification =
            await notifications.Reserve(second.Id, 123, 123, CancellationToken.None)
            ?? throw new InvalidOperationException("Missing second notification fixture");
        await notifications.Sent(firstNotification.Token, 42, CancellationToken.None);
        await notifications.Sent(secondNotification.Token, 43, CancellationToken.None);
        var transport = new FakeTelegramTransport { FailedEditMessage = 42 };
        var reviewLogger = new TelegramReviewLogger<TelegramOrderReviews>();
        using var services = new ServiceCollection()
            .AddSingleton<IHumanOrderReview>(new FakeHumanOrderReview(orders, broker, time))
            .BuildServiceProvider();
        var reviews = new TelegramOrderReviews(
            new("fake", 123, 123),
            notifications,
            orders,
            transport,
            services.GetRequiredService<IServiceScopeFactory>(),
            time,
            reviewLogger
        );
        TelegramClick[] clicks =
        [
            new(1, "first", 123, 123, 42, "a:" + firstNotification.Token),
            new(2, "second", 123, 123, 43, "a:" + secondNotification.Token),
        ];
        notifications.FailFind = storeFailure == "lookup";
        notifications.FailClaimReview = storeFailure == "claim";
        notifications.FailClaimReviewAfterWrite = storeFailure == "claimAcknowledgement";
        notifications.FailAdvance = storeFailure == "advance";
        notifications.FailAdvanceAfterWrite = storeFailure == "advanceAcknowledgement";
        orders.FailReviewedFind = storeFailure == "afterReview";
        orders.FailFind = storeFailure == "orderLookup";
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var offsets = new List<long>();
        transport.Polling = async (offset, cancellationToken) =>
        {
            offsets.Add(offset);
            if (offset < 3)
            {
                return clicks.Where(click => click.UpdateId >= offset).ToArray();
            }

            completed.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        };
        var logger = new TelegramReviewLogger<TelegramReviewWorker>();
        using var worker = new TelegramReviewWorker(reviews, notifications, transport, logger);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAny(completed.Task, logger.Warning.Task).WaitAsync(TimeSpan.FromSeconds(3));
            if (storeFailure is null)
            {
                TelegramReviewChecks.Check(
                    completed.Task.IsCompletedSuccessfully
                        && await notifications.Cursor(CancellationToken.None) == 3
                        && broker.Submissions == 2
                        && transport.LastEditedMessage == 43,
                    "A permanent first-message edit failure must not block a later legitimate click or cursor progress."
                );
                TelegramReviewChecks.Check(
                    offsets.SequenceEqual([0L, 3L]) && reviewLogger.Messages.Count == 1 && logger.Messages.Count == 0,
                    "Only the presentation warning is logged; polling reaches the next durable offset."
                );
                TelegramReviewChecks.Check(
                    transport.Answers.Count == 2
                        && transport.Answers[0].CallbackId == "first"
                        && transport.Answers[0].Text.Contains("แอป", StringComparison.Ordinal)
                        && !transport.Answers[0].Text.Contains("ข้อความนี้", StringComparison.Ordinal),
                    "A failed message edit must direct feedback to the app, without claiming the message was updated."
                );
            }
            else
            {
                var reviewed = storeFailure is "afterReview" or "advance" or "advanceAcknowledgement";
                TelegramReviewChecks.Check(
                    logger.Messages.Count == 1
                        && !completed.Task.IsCompleted
                        && await notifications.Cursor(CancellationToken.None)
                            == (storeFailure == "advanceAcknowledgement" ? 2 : 0)
                        && broker.Submissions == (reviewed ? 1 : 0),
                    "Durable-store failure interrupts processing; a cursor write can persist before its acknowledgement is lost."
                );
                TelegramReviewChecks.Check(
                    reviewLogger.Messages.Count == (storeFailure is "advance" or "advanceAcknowledgement" ? 1 : 0),
                    "Lookup, review claim and post-review store reads are never caught as presentation failures."
                );
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        TelegramReviewChecks.Check(
            !reviewLogger.ExceptionPayloadReceived
                && reviewLogger.Messages.All(message => !message.Contains("fixture-secret", StringComparison.Ordinal)),
            "Display failure logging must not include raw transport exceptions or bot tokens."
        );
        notifications.FailFind = false;
        notifications.FailClaimReview = false;
        notifications.FailClaimReviewAfterWrite = false;
        notifications.FailAdvance = false;
        notifications.FailAdvanceAfterWrite = false;
        orders.FailReviewedFind = false;
        orders.FailFind = false;
        var durableCursor = await notifications.Cursor(CancellationToken.None);
        var restartedReviews = new TelegramOrderReviews(
            new("fake", 123, 123),
            notifications,
            orders,
            transport,
            services.GetRequiredService<IServiceScopeFactory>(),
            time,
            reviewLogger
        );
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restartedOffsets = new List<long>();
        transport.Polling = async (offset, cancellationToken) =>
        {
            restartedOffsets.Add(offset);
            if (restartedOffsets.Count == 1)
            {
                // Deliberately replay even acknowledged clicks to verify durable review claims.
                return clicks;
            }

            restarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        };
        var restartLogger = new TelegramReviewLogger<TelegramReviewWorker>();
        using var restartWorker = new TelegramReviewWorker(restartedReviews, notifications, transport, restartLogger);
        await restartWorker.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAny(restarted.Task, restartLogger.Warning.Task).WaitAsync(TimeSpan.FromSeconds(3));
            TelegramReviewChecks.Check(
                restarted.Task.IsCompletedSuccessfully
                    && restartedOffsets.SequenceEqual([durableCursor, 3L])
                    && await notifications.Cursor(CancellationToken.None) == 3
                    && broker.Submissions == (storeFailure == "claimAcknowledgement" ? 1 : 2),
                "Restart resumes the durable cursor; replayed or uncertain review claims never resubmit."
            );
            TelegramReviewChecks.Check(
                (await notifications.Find(firstNotification.Token, CancellationToken.None))?.State
                    == OrderNotificationState.Reviewed
                    && (await notifications.Find(secondNotification.Token, CancellationToken.None))?.State
                        == OrderNotificationState.Reviewed,
                "Both exact notification claims remain durable across worker restart."
            );
            var expectedEdits = storeFailure switch
            {
                null => new[] { 42L, 43L, 42L, 43L },
                "advance" or "advanceAcknowledgement" => [42L, 42L, 43L],
                _ => [42L, 43L],
            };
            TelegramReviewChecks.Check(
                transport.EditAttempts.SequenceEqual(expectedEdits)
                    && !reviewLogger.ExceptionPayloadReceived
                    && reviewLogger.Messages.All(message =>
                        !message.Contains("fixture-secret", StringComparison.Ordinal)
                    ),
                "Only presentation is retried on replay, with no raw exception or token in the warning log."
            );
            Console.WriteLine(
                $"Telegram polling fixture {storeFailure ?? "editFailure"}: poll offsets {string.Join(',', offsets)}; restart offsets {string.Join(',', restartedOffsets)}; submissions {broker.Submissions}; edit attempts {transport.EditAttempts.Count}."
            );
        }
        finally
        {
            await restartWorker.StopAsync(CancellationToken.None);
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
