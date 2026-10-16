using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingFillNoticeChecks
{
    public static async Task Run()
    {
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
            notice.Text == "🟢 ซื้อ ETH · 1 เหรียญ · จ่าย ฿110.28",
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
            sell.Text == "🔴 ขาย ETH · 1 เหรียญ · รับ ฿119.70",
            "Sales show actual proceeds after fees without an approval prompt."
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
            notifier.Sends == 1 && notifier.LastText == notice.Text,
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

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
