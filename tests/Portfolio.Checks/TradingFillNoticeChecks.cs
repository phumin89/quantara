using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingFillNoticeChecks
{
    public static async Task Run()
    {
        await ConcurrentDelivery();
        using var fixture = new AutonomousFixture();
        await fixture.Activate();
        await fixture.Engine.Tick(CancellationToken.None);
        Check(fixture.Store.State.Alerts.All(alert => alert.Kind != "fill"), "Submission is not a confirmed purchase.");
        var order = fixture.Store.State.Live.Orders.Single();
        var fill = new TradingFill(
            Guid.NewGuid(),
            order.Id,
            "confirmed-fill",
            "ETH",
            TradeSide.Buy,
            1,
            110,
            110,
            .28m,
            fixture.Time.Now
        );
        fixture.Exchange.Observation = intent => new("partial", intent.BrokerOrderId, intent.Amount - 110.28m, [fill]);
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        await fixture.Engine.Tick(CancellationToken.None);
        var notice = fixture.Store.State.Alerts.Single(alert => alert.Kind == "fill");
        Check(
            notice.Text.ReplaceLineEndings("\n") == "🟢 ซื้อ ETH แล้ว\nจำนวน 1 ETH\nราคา ฿110\nจ่ายรวม ฿110.28",
            "Confirmed purchases use concise Thai and actual net cash."
        );
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        await fixture.Restart().Tick(CancellationToken.None);
        Check(
            fixture.Store.State.Alerts.Count(alert => alert.Kind == "fill") == 1,
            "Repeated reconciliation and restart never duplicate fill notices."
        );
        var sell = TradingFillNotice.Create(
            fill with
            {
                Side = TradeSide.Sell,
                GrossThb = 120,
                FeeThb = .3m,
            },
            fixture.Time.Now
        );
        Check(
            sell.Text.ReplaceLineEndings("\n") == "🔴 ขาย ETH แล้ว\nจำนวน 1 ETH\nราคา ฿110\nรับสุทธิ ฿119.70",
            "Sales show actual proceeds after fees without an approval prompt."
        );
        var decimalFill = fill with { Quantity = 1.234567891m, Price = 1_234.567891234m };
        Check(
            TradingFillNotice.Create(decimalFill, fixture.Time.Now).Text.ReplaceLineEndings("\n")
                == "🟢 ซื้อ ETH แล้ว\nจำนวน ≈1.23456789 ETH\nราคา ฿1,234.56789123\nจ่ายรวม ฿110.28",
            "Quantity truncates to eight decimals and marks lost precision while price groups decimals."
        );
        var tinyFill = fill with { Quantity = .00000000123m, Price = .00000000123m };
        Check(
            TradingFillNotice
                .Create(tinyFill, fixture.Time.Now)
                .Text.ReplaceLineEndings("\n")
                .Contains("จำนวน 0.00000000123 ETH\nราคา ฿0.00000000123", StringComparison.Ordinal),
            "Tiny positive quantity and price remain visible instead of becoming zero."
        );
        Check(
            TradingFillNotice
                .Create(decimalFill with { QuantityScale = 8 }, fixture.Time.Now)
                .Text.Contains("จำนวน 1.23456789 ETH", StringComparison.Ordinal),
            "Verified broker precision reports the actual credited quantity without false approximation."
        );
        var store = new FakeTradingDeliveryStore(fixture.Store.State with { Reports = [] });
        var notifier = new FakeTradingNotifier();
        var delivery = new TradingReportDelivery(store, notifier, fixture.Time);
        fixture.Time.Now = new DateTimeOffset(
            fixture.Time.Now.ToOffset(TimeSpan.FromHours(7)).Date,
            TimeSpan.FromHours(7)
        )
            .AddHours(23)
            .AddMinutes(59);
        await delivery.Tick(CancellationToken.None);
        Check(
            notifier.Sends == 1
                && notifier.LastText is { } delivered
                && delivered.StartsWith("🕘", StringComparison.Ordinal)
                && delivered.EndsWith(notice.Text, StringComparison.Ordinal),
            "Adaptive Telegram sends the fill note, keeping the daily report in the application."
        );
        Check(
            store.State.Reports.Count > 0 && store.State.Reports.All(report => report.Status == "local"),
            "Adaptive daily reports are retained locally."
        );
        Console.WriteLine(
            "Confirmed fill notifications: concise Thai, exact cash, deduplication and no daily Telegram summary passed."
        );
    }

    private static async Task ConcurrentDelivery()
    {
        using var fixture = new AutonomousFixture();
        var alert = new TradingAlert("fill:concurrent", "fill", "Confirmed purchase fixture", fixture.Time.Now);
        var store = new FakeTradingDeliveryStore(
            fixture.Store.State with
            {
                Alerts =
                [
                    new TradingAlert("old-fault", "fault", "Legacy fault", fixture.Time.Now),
                    new TradingAlert("fill:unknown", "fill", "Uncertain delivery", fixture.Time.Now, "unknown"),
                    new TradingAlert("fill:sending", "fill", "Interrupted delivery", fixture.Time.Now, "sending"),
                    alert,
                ],
            }
        );
        using var gate = new SemaphoreSlim(0, 1);
        var notifier = new FakeTradingNotifier
        {
            BeforeSend = () =>
                Check(
                    store.State.Alerts.Single(item => item.Key == alert.Key).Status == "sending"
                        && gate.CurrentCount == 1,
                    "Claim is durable and the trading lock is released before Telegram HTTP."
                ),
        };
        var delivery = new TradingReportDelivery(store, notifier, fixture.Time, gate);
        var pending = delivery.Tick(CancellationToken.None);
        store.State = store.State with { Revision = store.State.Revision + 1 };
        gate.Release();
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Check(
            notifier.Sends == 1 && store.State.Alerts.Single(item => item.Key == alert.Key).Status == "sent",
            "A concurrent quote update must not starve a pending fill notification."
        );
        await delivery.Tick(CancellationToken.None);
        Check(notifier.Sends == 1, "A confirmed send is never repeated.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
