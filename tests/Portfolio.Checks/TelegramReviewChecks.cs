using Microsoft.Extensions.DependencyInjection;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TelegramReviewChecks
{
    public static async Task Run()
    {
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
            using var services = new ServiceCollection()
                .AddSingleton<IHumanOrderReview>(new FakeHumanOrderReview(orders, broker, time))
                .BuildServiceProvider();
            var reviews = new TelegramOrderReviews(
                new("fake", 123, 123),
                notifications,
                orders,
                transport,
                services.GetRequiredService<IServiceScopeFactory>(),
                time
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

            broker.FailSubmit = scenario == "unknownSubmission";
            transport.FailEdit = scenario == "editFailure";
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
                    await reviews.Process(click, CancellationToken.None);
                }
            }
            catch (HttpRequestException) when (scenario == "editFailure") { }

            transport.FailEdit = false;
            await reviews.Process(click, CancellationToken.None);
            var submitted = scenario is "approve" or "unknownSubmission" or "editFailure" or "sell" or "concurrent";
            TelegramReviewChecks.Check(
                broker.Submissions == (submitted ? 1 : 0),
                "Only the first authenticated unexpired approve submits."
            );
            var result =
                await orders.Find(draft.Id, CancellationToken.None)
                ?? throw new InvalidOperationException("Missing fixture");
            TelegramReviewChecks.Check(
                scenario != "unknownSubmission" || result.State == OrderState.Unknown,
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

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
