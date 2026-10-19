using System.Globalization;
using Quantara.Portfolio.Application.Trading;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingReportDelivery(
    ITradingStore store,
    ITradingNotifier notifier,
    TimeProvider time,
    SemaphoreSlim? gate = null,
    ITradingNotificationReceiptStore? receipts = null
)
{
    private readonly TradingAlertDelivery alerts = new(store, notifier, time, gate, receipts);

    private int correctionCursor;

    public async Task Tick(CancellationToken cancellationToken)
    {
        if (notifier.IsConfigured)
        {
            for (var index = 0; index < 4; index++)
            {
                if (!await this.alerts.Deliver(null, cancellationToken))
                {
                    break;
                }
            }
        }

        await this.PrepareReports(cancellationToken);
    }

    public async Task PrepareReports(CancellationToken cancellationToken)
    {
        await this.alerts.Recover(null, cancellationToken);
        var now = time.GetUtcNow();
        var state = await store.Read(cancellationToken);
        var local = now.ToOffset(TimeSpan.FromHours(7));
        var today = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = new List<(string Date, TradingMode Mode)>();
        if (local.TimeOfDay >= new TimeSpan(23, 59, 0))
        {
            days.Add(
                (
                    today,
                    state.Reports.LastOrDefault(report => report.Date == today)?.Mode
                        ?? TradingReportDelivery.Mode(state)
                )
            );
        }

        var yesterday = local.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!state.Reports.Any(report => report.Date == yesterday))
        {
            var mode = TradingReportDelivery.PastMode(state, yesterday);
            var ledger = mode == TradingMode.Paper ? state.Paper : state.Live;
            if (ledger.OpenedAt < TradingReportDelivery.DayEnd(yesterday))
            {
                days.Add((yesterday, mode));
            }
        }

        var retained = state
            .Reports.Where(report => string.CompareOrdinal(report.Date, today) <= 0)
            .Select(report => (report.Date, report.Mode))
            .Distinct()
            .OrderBy(report => report.Date, StringComparer.Ordinal)
            .ThenBy(report => report.Mode)
            .ToArray();
        for (var index = 0; index < Math.Min(3, retained.Length); index++)
        {
            days.Add(retained[this.correctionCursor % retained.Length]);
            this.correctionCursor = (this.correctionCursor + 1) % retained.Length;
        }

        foreach (var day in days.Distinct())
        {
            await this.Prepare(day.Date, day.Mode, now, cancellationToken);
        }

        if (!notifier.IsConfigured)
        {
            return;
        }

        await this.DeliverReport(now, cancellationToken);
    }

    private async Task<bool> Save(TradingState state, long expectedRevision, CancellationToken cancellationToken)
    {
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        try
        {
            return await store.Save(state, expectedRevision, cancellationToken);
        }
        finally
        {
            gate?.Release();
        }
    }

    private async Task Prepare(string date, TradingMode mode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = await store.Read(cancellationToken);
        var previous = state
            .Reports.Where(report => report.Date == date && report.Mode == mode)
            .MaxBy(report => report.Revision);
        if (previous?.Status is "sending" or "editing")
        {
            return;
        }

        var ledger = mode == TradingMode.Paper ? state.Paper : state.Live;
        var candidate = TradingDailyReportBuilder.Build(
            ledger,
            mode,
            date,
            state.Decisions.Where(decision => decision.Mode == mode).ToArray(),
            state.Fault,
            now,
            (previous?.Revision ?? 0) + 1,
            previous,
            state.Mode
        );
        if (previous is not null && candidate.Fingerprint == previous.Fingerprint)
        {
            if (previous.ClosedAt is null && candidate.ClosedAt is not null)
            {
                var closed = previous with { ClosedAt = candidate.ClosedAt };
                await this.Save(
                    state with
                    {
                        Revision = state.Revision + 1,
                        Reports = state.Reports.Select(report => report == previous ? closed : report).ToArray(),
                    },
                    state.Revision,
                    cancellationToken
                );
            }

            return;
        }

        candidate = candidate with
        {
            MessageId = previous?.MessageId,
            Status =
                state.Rules.Adaptive is not null ? "local"
                : !notifier.IsConfigured ? "unconfigured"
                : previous is null || previous.Status is "prepared" or "unconfigured" || previous.MessageId is > 0
                    ? "prepared"
                : "unknown",
        };
        await this.Save(
            state with
            {
                Revision = state.Revision + 1,
                Reports = state.Reports.Append(candidate).ToArray(),
            },
            state.Revision,
            cancellationToken
        );
    }

    public async Task DeliverAlert(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 200)
        {
            throw new ArgumentException("Trading alert identity exceeds its limit.", nameof(key));
        }

        await this.alerts.Deliver(key, cancellationToken);
    }

    private async Task DeliverReport(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = await store.Read(cancellationToken);
        if (state.Rules.Adaptive is not null)
        {
            return;
        }

        var report = state
            .Reports.Where(item =>
                item.Status == "prepared"
                && !state.Reports.Any(later =>
                    later.Date == item.Date && later.Mode == item.Mode && later.Revision > item.Revision
                )
            )
            .OrderBy(item => item.Date, StringComparer.Ordinal)
            .FirstOrDefault();
        if (report is null)
        {
            return;
        }

        var claimed = report with { Status = report.MessageId is > 0 ? "editing" : "sending", ClaimedAt = now };
        if (
            !await this.Save(
                state with
                {
                    Revision = state.Revision + 1,
                    Reports = state.Reports.Select(item => item == report ? claimed : item).ToArray(),
                },
                state.Revision,
                cancellationToken
            )
        )
        {
            return;
        }

        var messageId = report.MessageId;
        try
        {
            var text =
                report.Revision > 1
                    ? report.Text + "\nแก้ไขครั้งที่: " + (report.Revision - 1).ToString(CultureInfo.InvariantCulture)
                    : report.Text;
            if (messageId is > 0)
            {
                await notifier.Edit(messageId.Value, text, cancellationToken);
            }
            else
            {
                messageId = await notifier.Send(text, cancellationToken);
                if (messageId <= 0)
                {
                    throw new InvalidOperationException("Trading report delivery was not acknowledged.");
                }
            }
        }
        catch (Exception)
        {
            await this.FinishReport(claimed, "unknown", messageId);
            throw;
        }

        await this.FinishReport(claimed, "sent", messageId);
    }

    private async Task FinishReport(TradingDailyReport report, string status, long? messageId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var state = await store.Read(CancellationToken.None);
            if (
                await this.Save(
                    state with
                    {
                        Revision = state.Revision + 1,
                        Reports = state
                            .Reports.Select(item =>
                                item.Date == report.Date
                                && item.Mode == report.Mode
                                && item.Revision == report.Revision
                                && item.Status == report.Status
                                    ? item with
                                    {
                                        Status = status,
                                        MessageId = messageId,
                                    }
                                    : item
                            )
                            .ToArray(),
                    },
                    state.Revision,
                    CancellationToken.None
                )
            )
            {
                return;
            }
        }
    }

    private static TradingMode Mode(TradingState state) =>
        state.Mode == TradingMode.Paper ? TradingMode.Paper
        : state.Mode == TradingMode.Stopped
            ? (state.Reports.Count == 0 ? null : (TradingMode?)state.Reports[^1].Mode)
                ?? (state.Live.FundingThb > 0 ? TradingMode.AutoLive : TradingMode.Paper)
        : state.Mode;

    private static TradingMode PastMode(TradingState state, string date)
    {
        var end = TradingReportDelivery.DayEnd(date);
        var decision = state
            .Decisions.Where(item => item.At < end && item.At >= end.AddDays(-1))
            .MaxBy(item => item.At);
        return decision is not null
            ? decision.Mode == TradingMode.Paper
                ? TradingMode.Paper
                : TradingMode.AutoLive
            : state
                .Reports.Where(report => string.CompareOrdinal(report.Date, date) < 0)
                .MaxBy(report => report.Date)
                ?.Mode
                ?? (state.Live.FundingThb > 0 && state.Live.OpenedAt < end ? TradingMode.AutoLive : TradingMode.Paper);
    }

    private static DateTimeOffset DayEnd(string date) =>
        new DateTimeOffset(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.FromHours(7))
            .AddDays(1)
            .ToUniversalTime();
}
