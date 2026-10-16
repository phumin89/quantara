using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingDeliveryChecks
{
    public static async Task Run()
    {
        var day = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.FromHours(7));
        var state = TradingStateFactory.Create(day);
        state = state with
        {
            Paper = state.Paper with
            {
                Equity =
                [
                    new TradingEquityMark(day, 1000, 1000),
                    new TradingEquityMark(day.AddHours(23).AddMinutes(58), 1000, 1000),
                ],
            },
        };
        var store = new FakeTradingDeliveryStore(state);
        var clock = new OrderCheckTime { Now = day.AddHours(23).AddMinutes(58) };
        var notifier = new FakeTradingNotifier();
        notifier.BeforeSend = () =>
            TradingDeliveryChecks.Check(
                store.State.Reports.Any(report => report.Status == "sending" && report.ClaimedAt is not null),
                "Report claim must be durable before send."
            );
        notifier.BeforeEdit = () =>
            TradingDeliveryChecks.Check(
                store.State.Reports.Any(report => report.Status == "editing" && report.MessageId == 42),
                "Correction claim must be durable and use the known message."
            );
        var delivery = new TradingReportDelivery(store, notifier, clock);
        await delivery.Tick(CancellationToken.None);
        TradingDeliveryChecks.Check(
            notifier.Sends == 0 && store.State.Reports.Count == 0,
            "The daily summary is not sent before 23:59 Bangkok."
        );
        clock.Now = day.AddHours(23).AddMinutes(59);
        await delivery.Tick(CancellationToken.None);
        await delivery.Tick(CancellationToken.None);
        TradingDeliveryChecks.Check(
            notifier.Sends == 1 && notifier.Edits == 0 && store.State.Reports.Single().MessageId == 42,
            "The 23:59 report sends once and retains its message ID."
        );
        clock.Now = day.AddDays(1);
        await delivery.Tick(CancellationToken.None);
        var closed =
            store.State.Reports.MaxBy(report => report.Revision)
            ?? throw new InvalidOperationException("Closed report is missing.");
        TradingDeliveryChecks.Check(
            closed.ClosedAt is not null
                && closed.OperatingMode == TradingMode.Paper
                && notifier.Sends == 1
                && notifier.Edits == 1,
            "Midnight finalizes the existing report through an edit."
        );
        var late = new TradingFill(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "late-fill",
            "BTC",
            TradeSide.Buy,
            0.1m,
            1000m,
            100m,
            0.25m,
            day.AddHours(23).AddMinutes(59).AddSeconds(30)
        );
        store.State = store.State with
        {
            Mode = TradingMode.AutoLive,
            Paper = store.State.Paper with
            {
                CashThb = 899.75m,
                Fills = [late],
                Equity = store
                    .State.Paper.Equity.Append(new TradingEquityMark(day.AddDays(1).AddMinutes(1), 9000, 1000))
                    .ToArray(),
            },
        };
        clock.Now = day.AddDays(1).AddMinutes(2);
        await delivery.Tick(CancellationToken.None);
        await delivery.Tick(CancellationToken.None);
        var corrected =
            store.State.Reports.MaxBy(report => report.Revision)
            ?? throw new InvalidOperationException("Corrected report is missing.");
        TradingDeliveryChecks.Check(
            notifier.Sends == 1
                && notifier.Edits == 2
                && notifier.LastMessageId == 42
                && corrected.Mode == TradingMode.Paper
                && corrected.OperatingMode == TradingMode.Paper
                && corrected.EndingEquityThb is null
                && corrected.Text.Contains("เงินสด: 899.75", StringComparison.Ordinal),
            "Late fills correct the original ledger/message once; old equity that omitted the late fill becomes Unknown."
        );

        await TradingDeliveryChecks.UnknownSend(state, clock, day);
        await TradingDeliveryChecks.UnknownEdit(state, clock, day);
        await TradingDeliveryChecks.LostClaim(state, clock, day);
        await TradingDeliveryChecks.Alerts(state, clock, day);
        Console.WriteLine(
            "Trading delivery checks passed: durable claims, Bangkok midnight, late corrections, uncertain sends and alert deduplication."
        );
    }

    private static async Task UnknownSend(TradingState state, OrderCheckTime clock, DateTimeOffset day)
    {
        var store = new FakeTradingDeliveryStore(state);
        var notifier = new FakeTradingNotifier { FailSend = true };
        clock.Now = day.AddHours(23).AddMinutes(59);
        await TradingDeliveryChecks.Uncertain(new TradingReportDelivery(store, notifier, clock));
        TradingDeliveryChecks.Check(
            store.State.Reports.Single().Status == "unknown" && store.State.Reports.Single().MessageId is null,
            "An uncertain send is retained without a guessed message ID."
        );
        notifier.FailSend = false;
        clock.Now = day.AddDays(1).AddMinutes(1);
        await new TradingReportDelivery(store, notifier, clock).Tick(CancellationToken.None);
        TradingDeliveryChecks.Check(
            notifier.Sends == 1 && notifier.Edits == 0,
            "Restart and midnight cannot resend an uncertain report."
        );

        store = new FakeTradingDeliveryStore(state)
        {
            RejectSave = candidate => candidate.Reports.Any(report => report.Status == "sent"),
        };
        notifier = new FakeTradingNotifier();
        clock.Now = day.AddHours(23).AddMinutes(59);
        await new TradingReportDelivery(store, notifier, clock).Tick(CancellationToken.None);
        await new TradingReportDelivery(store, notifier, clock).Tick(CancellationToken.None);
        TradingDeliveryChecks.Check(
            notifier.Sends == 1 && store.State.Reports.Single().Status == "sending",
            "Loss of send acknowledgement storage must never resend."
        );
    }

    private static async Task LostClaim(TradingState state, OrderCheckTime clock, DateTimeOffset day)
    {
        var store = new FakeTradingDeliveryStore(state)
        {
            RejectSave = candidate => candidate.Reports.Any(report => report.Status == "sending"),
        };
        var notifier = new FakeTradingNotifier();
        clock.Now = day.AddHours(23).AddMinutes(59);
        await new TradingReportDelivery(store, notifier, clock).Tick(CancellationToken.None);
        TradingDeliveryChecks.Check(notifier.Sends == 0, "Losing the durable claim cannot send.");
    }

    private static async Task UnknownEdit(TradingState state, OrderCheckTime clock, DateTimeOffset day)
    {
        var store = new FakeTradingDeliveryStore(state);
        var notifier = new FakeTradingNotifier();
        clock.Now = day.AddHours(23).AddMinutes(59);
        var delivery = new TradingReportDelivery(store, notifier, clock);
        await delivery.Tick(CancellationToken.None);
        clock.Now = day.AddDays(1);
        notifier.FailEdit = true;
        await TradingDeliveryChecks.Uncertain(delivery);
        notifier.FailEdit = false;
        await new TradingReportDelivery(store, notifier, clock).Tick(CancellationToken.None);
        TradingDeliveryChecks.Check(
            notifier.Sends == 1
                && notifier.Edits == 1
                && store.State.Reports.MaxBy(report => report.Revision)?.Status == "unknown",
            "An uncertain correction is not retried when its fingerprint is unchanged."
        );
    }

    private static async Task Alerts(TradingState state, OrderCheckTime clock, DateTimeOffset day)
    {
        var store = new FakeTradingDeliveryStore(
            state with
            {
                Alerts = [new TradingAlert("risk", "risk", "Risk fixture", day)],
            }
        );
        var notifier = new FakeTradingNotifier { FailSend = true };
        notifier.BeforeSend = () =>
            TradingDeliveryChecks.Check(
                store.State.Alerts.Single().Status == "sending",
                "Alert claim must be durable before send."
            );
        clock.Now = day.AddHours(12);
        await TradingDeliveryChecks.Uncertain(new TradingReportDelivery(store, notifier, clock));
        notifier.FailSend = false;
        await new TradingReportDelivery(store, notifier, clock).Tick(CancellationToken.None);
        TradingDeliveryChecks.Check(
            notifier.Sends == 1 && store.State.Alerts.Single().Status == "unknown",
            "Uncertain alerts are never automatically resent."
        );
    }

    private static async Task Uncertain(TradingReportDelivery delivery)
    {
        try
        {
            await delivery.Tick(CancellationToken.None);
        }
        catch (HttpRequestException)
        {
            return;
        }

        throw new InvalidOperationException("An uncertain notification was expected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
