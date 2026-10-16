using Quantara.Portfolio.Application;
using Quantara.Portfolio.Application.Trading;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingReportChecks
{
    public static Task Run()
    {
        var day = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.FromHours(7));
        var intent = Guid.NewGuid();
        var fills = new[]
        {
            new TradingFill(Guid.NewGuid(), intent, "b", "BTC", TradeSide.Buy, 1, 100, 100, 1, day.AddMinutes(1)),
            new TradingFill(
                Guid.NewGuid(),
                intent,
                "s",
                "BTC",
                TradeSide.Sell,
                1,
                110,
                110,
                1,
                day.AddHours(23).AddMinutes(59),
                10,
                8
            ),
            new TradingFill(Guid.NewGuid(), intent, "next", "BTC", TradeSide.Buy, 1, 100, 100, 1, day.AddDays(1)),
        };
        var ledger = new TradingLedger(
            1000,
            1000,
            day,
            [],
            [new TradingIntent(intent, "BTC", TradeSide.Buy, 100, 100, 0, day, 1, "r", "x", "submitted")],
            fills,
            [
                new TradingEquityMark(day.AddMinutes(-1), 1000, 1000),
                new TradingEquityMark(day.AddHours(23).AddMinutes(58), 1008, 1000),
            ]
        );
        var report = TradingDailyReportBuilder.Build(ledger, TradingMode.Paper, "2026-10-16", [], null, day.AddDays(1));
        Check(
            report.RealizedNetThb == 8 && report.EndingEquityThb == 1008 && report.Status == "prepared",
            "daily report uses Bangkok boundaries and known realized net"
        );
        Check(
            report.Fingerprint
                == TradingDailyReportBuilder
                    .Build(ledger, TradingMode.Paper, "2026-10-16", [], null, day.AddDays(1), 9)
                    .Fingerprint,
            "fingerprint excludes now and revision"
        );
        Check(report.StartingEquityThb == 1000, "starting equity uses prior mark");
        Check(
            report.Text.Contains("เงินสด: 1008", StringComparison.Ordinal)
                && report.Text.Contains("ออเดอร์เปิด: Unknown", StringComparison.Ordinal),
            "closed report cash is reconstructed from dated fills and historical open-order status remains unknown"
        );
        var bought = TradingLedgerAccounting.Apply(
            new TradingLedger(1000, 1000, day, [], [], [], [new TradingEquityMark(day, 1000, 1000)]),
            new TradingFill(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "held",
                "BTC",
                TradeSide.Buy,
                0.1m,
                1000m,
                100m,
                0.25m,
                day.AddMinutes(1)
            )
        );
        var marked = bought with
        {
            Equity = bought
                .Equity.Append(new TradingEquityMark(day.AddHours(23).AddMinutes(59), 1009.75m, 1000))
                .ToArray(),
        };
        var held = TradingDailyReportBuilder.Build(marked, TradingMode.Paper, "2026-10-16", [], null, day.AddDays(1));
        Check(
            held.Text.Contains("กำไรที่ยังไม่รับรู้: 9.75", StringComparison.Ordinal)
                && held.Text.Contains("มูลค่าที่ลงทุน: 110", StringComparison.Ordinal),
            "exposure and unrealized profit require known marks, funding and retained cost evidence"
        );
        Check(
            TradingDailyReportBuilder
                .Build(
                    ledger with
                    {
                        FundingThb = 0,
                        Equity = [],
                    },
                    TradingMode.AutoLive,
                    "2026-10-16",
                    [],
                    null,
                    day.AddDays(1)
                )
                .RealizedNetThb
                is null,
            "unfunded live realized result is unknown"
        );
        var resized = ledger with
        {
            FundingThb = 2000,
            Equity = ledger.Equity.Append(new TradingEquityMark(day.AddDays(1).AddHours(1), 2008, 2000)).ToArray(),
        };
        var prior = TradingDailyReportBuilder.Build(
            resized,
            TradingMode.Paper,
            "2026-10-16",
            [],
            null,
            day.AddDays(2),
            previous: report
        );
        Check(
            prior.Text.Contains("เงินสด: 1008", StringComparison.Ordinal) && prior.Fingerprint == report.Fingerprint,
            "Future funding changes do not rewrite past cash or equity."
        );
        Console.WriteLine("Trading report checks passed.");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
