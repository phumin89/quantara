using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;
using System.Text.Json;

namespace Portfolio.Checks;

public static class TelegramMessageChecks
{
    public static async Task Run()
    {
        var normal = Proposal("Signal: momentum", TradeSide.Buy, 1000m, 153.39m);
        var normalText = TelegramTransport.Text(normal, "⏳ Awaiting your approval");
        Check(normalText.Contains("ซื้อ NEAR", StringComparison.Ordinal), "buy message names the coin in Thai");
        Check(normalText.Contains("วงเงิน", StringComparison.Ordinal), "buy message labels the THB budget");
        Check(normalText.Contains("1,000", StringComparison.Ordinal), "buy message preserves the budget amount");
        Check(normalText.Contains("153.39", StringComparison.Ordinal), "buy message preserves the limit price");
        Check(normalText.Contains("2.50", StringComparison.Ordinal), "buy message shows the estimated fee");
        Check(
            normalText.Contains("รออนุมัติ", StringComparison.Ordinal),
            "pending message states that approval is awaited"
        );
        Check(
            normalText.Contains("อนุมัติก่อน", StringComparison.Ordinal),
            "pending message explains approval is required"
        );

        var submittedProposal = normal with { State = OrderState.Submitted };
        var submitted = TelegramTransport.Text(submittedProposal, "Submitted");
        Check(submitted.Contains("ส่งคำสั่งแล้ว", StringComparison.Ordinal), "submitted message reports sent state");
        Check(
            submitted.Contains("ยังไม่ยืนยัน", StringComparison.Ordinal),
            "submitted message reports unconfirmed state"
        );
        foreach (var forbidden in new[] { "อนุมัติก่อน", "Approve submits", "Approval expires", "รออนุมัติ" })
        {
            Check(
                !submitted.Contains(forbidden, StringComparison.Ordinal),
                "submitted message omits pending instruction: " + forbidden
            );
        }

        var planReason =
            "Risk <b>high</b>; never assume execution.\nTarget (plan): THB 155.20\nInvalidation (plan): THB 152.59\nReview by: 2026-10-15 21:27 +07:00. Exit/cancel requires review; not automatic.";
        var plan = Proposal(planReason, TradeSide.Buy, 1000m, 153.39m);
        var planText = TelegramTransport.Text(plan with { State = OrderState.Submitted }, "Submitted");
        Check(planText.Contains("เป้าขาย", StringComparison.Ordinal), "plan message labels the sell target");
        Check(planText.Contains("ทบทวนแผน", StringComparison.Ordinal), "plan message labels plan review");
        Check(
            planText.Contains("155.20", StringComparison.Ordinal)
                && planText.Contains("152.59", StringComparison.Ordinal),
            "plan message preserves target and invalidation"
        );
        Check(planText.Contains("21:27", StringComparison.Ordinal), "plan message preserves review time");
        Check(
            planText.Contains("<blockquote expandable>", StringComparison.Ordinal)
                && planText.Contains("Risk &lt;b&gt;high&lt;/b&gt;", StringComparison.Ordinal),
            "plan message retains escaped reason in expandable quote"
        );
        var summary = planText[..planText.IndexOf("<blockquote expandable>", StringComparison.Ordinal)];
        Check(summary.Contains("155.20", StringComparison.Ordinal) && summary.Contains("152.59", StringComparison.Ordinal) && summary.Contains("21:27", StringComparison.Ordinal), "plan summary exposes trusted target, invalidation and review time");
        Check(!summary.Contains("999.99", StringComparison.Ordinal), "plan summary excludes embedded fake target marker");
        var sellText = TelegramTransport.Text(
            Proposal(planReason, TradeSide.Sell, 0.25m, 153.39m),
            "⏳ Awaiting your approval"
        );
        Check(sellText.Contains("0.25 NEAR", StringComparison.Ordinal), "sell message shows coin quantity");
        Check(
            !sellText.Contains("วงเงิน", StringComparison.Ordinal),
            "sell message does not call coin quantity a THB budget"
        );

        var unsafeText = TelegramTransport.Text(
            Proposal("<script>alert(1)</script> <b>risk</b>", TradeSide.Buy, 0.000000000000000001m, 153.39m),
            "<b>Unknown</b>"
        );
        Check(
            unsafeText.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", StringComparison.Ordinal),
            "reason HTML is escaped"
        );
        Check(unsafeText.Contains("&lt;b&gt;Unknown&lt;/b&gt;", StringComparison.Ordinal), "status HTML is escaped");
        Check(
            unsafeText.Contains("0.000000000000000001", StringComparison.Ordinal),
            "tiny exact decimal remains unchanged"
        );

        var partial = TelegramTransport.Text(Proposal("partial fill", TradeSide.Buy, 1000m, 153.39m) with { State = OrderState.PartiallyFilled }, "Filled 0.4");
        Check(partial.Contains("บางส่วน", StringComparison.Ordinal) && !partial.Contains("ครบ", StringComparison.Ordinal), "partial state is honestly described as partial");
        var unknown = TelegramTransport.Text(Proposal("unknown result", TradeSide.Buy, 1000m, 153.39m) with { State = OrderState.Unknown }, "Submitted");
        Check(unknown.Contains("ไม่ทราบ", StringComparison.Ordinal) && !unknown.Contains("ส่งคำสั่งแล้ว", StringComparison.Ordinal), "unknown state never claims successful submission");
        var filled = TelegramTransport.Text(Proposal("filled", TradeSide.Buy, 1000m, 153.39m) with { State = OrderState.Filled }, "Filled");
        Check(filled.Contains("ครบ", StringComparison.Ordinal), "filled state is described as complete");
        var expired = TelegramTransport.Text(Proposal("expired", TradeSide.Buy, 1000m, 153.39m), "⌛ Expired · No order submitted");
        Check(expired.Contains("⌛ Expired · No order submitted", StringComparison.Ordinal) && !expired.Contains("Approval expires", StringComparison.Ordinal), "expired pending order has no approval deadline");
        var interrupted = TelegramTransport.Text(Proposal("review interrupted", TradeSide.Buy, 1000m, 153.39m) with { State = OrderState.Unknown }, "Review interrupted · submission status is uncertain");
        Check(interrupted.Contains("ไม่ทราบ", StringComparison.Ordinal) && interrupted.Contains("uncertain", StringComparison.Ordinal), "interrupted review retains honest uncertainty");

        var fakeMarker = Proposal(
            "embedded Target (plan): THB 999.99 / Invalidation(plan) 1.11 / Review by 2020-01-01 00:00 +07:00; final note.\nTarget (plan): THB 155.20\nInvalidation (plan): THB 152.59\nReview by: 2026-10-15 21:27 +07:00. Exit/cancel requires review; not automatic.",
            TradeSide.Buy,
            1000m,
            153.39m
        );
        var fakeText = TelegramTransport.Text(fakeMarker with { State = OrderState.Submitted }, "Submitted");
        Check(
            fakeText.Contains("155.20", StringComparison.Ordinal)
                && fakeText.Contains("152.59", StringComparison.Ordinal)
                && fakeText.Contains("21:27", StringComparison.Ordinal),
            "parser uses the anchored final plan suffix"
        );
        var malformed = TelegramTransport.Text(
            Proposal(
                "Target (plan): THB155.20 / Invalidation(plan)152.59 / Review by2026-10-15 21:27",
                TradeSide.Buy,
                1000m,
                153.39m
            ),
            "Submitted"
        );
        Check(
            !malformed.Contains("เป้าขาย", StringComparison.Ordinal)
                && malformed.Contains("Target (plan)", StringComparison.Ordinal),
            "malformed plan suffix stays untrusted and fully retained"
        );
        using var telegramHttp = new TelegramHttpHandler();
        using var telegramClient = new HttpClient(telegramHttp) { BaseAddress = new Uri("https://telegram.invalid/") };
        var transport = new TelegramTransport(telegramClient, new("123:fixture-token", 123, 123));
        var messageId = await transport.Send(normal, "a-token", CancellationToken.None);
        using var sendBody = JsonDocument.Parse(telegramHttp.RequestBody ?? throw new InvalidOperationException("Missing Telegram send body."));
        var sendText = sendBody.RootElement.GetProperty("text").GetString() ?? throw new InvalidOperationException("Missing Telegram send text.");
        var sendMarkup = sendBody.RootElement.GetProperty("reply_markup").GetRawText();
        Check(sendText.Contains("ซื้อ NEAR", StringComparison.Ordinal) && sendMarkup.Contains("อนุมัติ", StringComparison.Ordinal) && sendMarkup.Contains("ปฏิเสธ", StringComparison.Ordinal), "real send payload has Thai copy and review buttons");
        Check(sendMarkup.Contains("a-token", StringComparison.Ordinal) && sendMarkup.Contains("r-token", StringComparison.Ordinal), "send buttons bind identical approve and reject tokens");
        await transport.Edit(messageId, normal, "<b>unsafe</b>", CancellationToken.None);
        using var editBody = JsonDocument.Parse(telegramHttp.RequestBody ?? throw new InvalidOperationException("Missing Telegram edit body."));
        var editText = editBody.RootElement.GetProperty("text").GetString() ?? throw new InvalidOperationException("Missing Telegram edit text.");
        Check(!editBody.RootElement.GetProperty("reply_markup").GetRawText().Contains("อนุมัติ", StringComparison.Ordinal) && !editText.Contains("Approval expires", StringComparison.Ordinal), "edit removes review keyboard and deadline");
        Check(editText.Contains("&lt;b&gt;unsafe&lt;/b&gt;", StringComparison.Ordinal), "edit escapes status once");
    }

    private static OrderProposal Proposal(string reason, TradeSide side, decimal amount, decimal price) =>
        new(
            Guid.NewGuid(),
            "NEAR",
            side,
            amount,
            price,
            price,
            reason,
            new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2026, 10, 15, 21, 27, 0, TimeSpan.FromHours(7)),
            OrderState.Pending,
            null,
            null
        );

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }

        Console.WriteLine("Passed: " + message);
    }
}
