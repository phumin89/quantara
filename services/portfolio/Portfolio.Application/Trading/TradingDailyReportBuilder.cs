using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application.Trading;

public static class TradingDailyReportBuilder
{
    public static TradingDailyReport Build(
        TradingLedger ledger,
        TradingMode mode,
        string date,
        IReadOnlyList<TradingDecisionRecord> decisions,
        string? fault,
        DateTimeOffset now,
        int revision = 1,
        TradingDailyReport? previous = null,
        TradingMode? operatingMode = null
    )
    {
        if (
            !DateTime.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localDate
            )
        )
        {
            throw new ArgumentException("Date must be yyyy-MM-dd.", nameof(date));
        }

        var start = new DateTimeOffset(
            DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified),
            TimeSpan.FromHours(7)
        ).ToUniversalTime();
        var end = start.AddDays(1);
        if (previous is not null && (previous.Date != date || previous.Mode != mode))
        {
            throw new ArgumentException(
                "Frozen marks must belong to the same report day and ledger.",
                nameof(previous)
            );
        }

        var instant = now < end ? now : end;
        var recordedMode = now >= end ? previous?.OperatingMode : operatingMode;
        var history = ledger
            .Fills.Where(fill => fill.OccurredAt < end && fill.OccurredAt <= instant)
            .OrderBy(fill => fill.OccurredAt)
            .ThenBy(fill => fill.Id)
            .ToArray();
        if (
            history.Select(fill => fill.Id).Distinct().Count() != history.Length
            || history.Select(fill => fill.BrokerFillId).Distinct(StringComparer.Ordinal).Count() != history.Length
        )
        {
            throw new InvalidDataException("Duplicate fill IDs in daily ledger.");
        }

        var fills = history.Where(fill => fill.OccurredAt >= start).ToArray();
        var sells = fills.Where(fill => fill.Side == TradeSide.Sell).ToArray();
        var funding = ledger.Equity.Where(mark => mark.At <= instant).MaxBy(mark => mark.At)?.FundingThb;
        var knownFunding = ledger.OpenedAt <= instant && funding > 0m;
        var evidenceHash = Hash(history);
        var startingEvidenceHash = Hash(history.Where(fill => fill.OccurredAt < start).ToArray());
        var changedEndEvidence = previous?.ClosedAt is not null && previous.EvidenceHash != evidenceHash;
        var changedStartEvidence =
            previous?.ClosedAt is not null && previous.StartingEvidenceHash != startingEvidenceHash;
        decimal? realizedNet = sells.All(fill => fill.RealizedNetThb.HasValue)
            ? sells.Sum(fill => fill.RealizedNetThb.GetValueOrDefault())
            : null;
        decimal? realizedGross = sells.All(fill => fill.RealizedGrossThb.HasValue)
            ? sells.Sum(fill => fill.RealizedGrossThb.GetValueOrDefault())
            : null;
        decimal? cash = knownFunding
            ? funding
                + history.Sum(fill =>
                    fill.Side == TradeSide.Buy ? -fill.GrossThb - fill.FeeThb : fill.GrossThb - fill.FeeThb
                )
            : null;
        var quantities = history
            .GroupBy(fill => fill.Symbol, StringComparer.Ordinal)
            .Select(group => group.Sum(fill => fill.Side == TradeSide.Buy ? fill.Quantity : -fill.Quantity))
            .ToArray();
        int? positions =
            knownFunding && quantities.All(quantity => quantity >= 0m)
                ? quantities.Count(quantity => quantity > 0m)
                : null;
        var startingMark = ledger.Equity.Where(mark => mark.At <= start).MaxBy(mark => mark.At);
        var endingMark = ledger.Equity.Where(mark => mark.At >= start && mark.At <= instant).MaxBy(mark => mark.At);
        decimal? starting =
            changedStartEvidence ? null
            : previous?.ClosedAt is not null ? previous.StartingEquityThb
            : startingMark is { EquityThb: not null } && start - startingMark.At <= TimeSpan.FromMinutes(2)
                ? startingMark.EquityThb
            : null;
        decimal? ending =
            changedEndEvidence ? null
            : previous?.ClosedAt is not null ? previous.EndingEquityThb
            : endingMark is { EquityThb: not null } && instant - endingMark.At <= TimeSpan.FromMinutes(2)
                ? endingMark.EquityThb
            : null;
        decimal? exposure =
            ending.HasValue && cash.HasValue && ending >= cash && (positions > 0 || positions == 0 && ending == cash)
                ? ending - cash
                : null;
        var priorSells = history.Where(fill => fill.Side == TradeSide.Sell).ToArray();
        decimal? remainingCost =
            knownFunding && priorSells.All(fill => fill.RealizedNetThb.HasValue)
                ? funding + priorSells.Sum(fill => fill.RealizedNetThb.GetValueOrDefault()) - cash
                : null;
        decimal? unrealized =
            exposure.HasValue && remainingCost >= 0m && (positions > 0 || positions == 0 && remainingCost == 0m)
                ? exposure - remainingCost
                : null;
        int? openOrders =
            now < end
                ? ledger.Orders.Count(order => order.CreatedAt <= instant && TradingLedgerAccounting.Active(order))
                : null;
        var completed = ledger
            .Orders.Where(order =>
                order.Side == TradeSide.Sell
                && order.Status == "filled"
                && order.Remaining == 0m
                && !ledger.Fills.Any(fill =>
                    fill.IntentId == order.Id && (fill.OccurredAt >= end || fill.OccurredAt > instant)
                )
            )
            .Select(order => order.Id)
            .ToHashSet();
        var results = history
            .Where(fill => fill.Side == TradeSide.Sell && completed.Contains(fill.IntentId))
            .GroupBy(fill => fill.IntentId)
            .Where(group => group.Max(fill => fill.OccurredAt) >= start)
            .Select(group =>
                group.All(fill => fill.RealizedNetThb.HasValue)
                    ? group.Sum(fill => fill.RealizedNetThb.GetValueOrDefault())
                    : (decimal?)null
            )
            .ToArray();
        var waits = decisions.Count(decision =>
            decision.Mode == mode
            && decision.At >= start
            && decision.At < end
            && decision.At <= now
            && decision.Action == TradingAction.Wait
        );
        var reportFault =
            now >= end ? "Unknown (ไม่มีประวัติสถานะย้อนหลัง)"
            : string.IsNullOrWhiteSpace(fault) ? "ไม่มี"
            : fault;
        var text = string.Join(
            "\n",
            new[]
            {
                $"รายงาน: {date}",
                $"สมุดบัญชี: {mode}{(mode == TradingMode.Paper ? " (จำลอง ไม่มีคำสั่งจริง)" : string.Empty)}",
                $"สถานะรอบรายงาน: {(recordedMode.HasValue ? recordedMode.Value.ToString() : "Unknown")}",
                $"ทุนเริ่มต้น: {Format(starting)} | ทุนสิ้นสุด: {Format(ending)} | เงินสด: {Format(cash)}",
                $"ออเดอร์ซื้อ: {fills.Where(fill => fill.Side == TradeSide.Buy).Select(fill => fill.IntentId).Distinct().Count()} | ออเดอร์ขาย: {sells.Select(fill => fill.IntentId).Distinct().Count()} | การจับคู่: {fills.Length}",
                $"กำไรขั้นต้น: {Format(realizedGross, knownFunding)} | กำไรสุทธิ: {Format(realizedNet, knownFunding)} | ค่าธรรมเนียมสุทธิ: {Format(fills.Sum(fill => fill.FeeThb))}",
                $"กำไรที่ยังไม่รับรู้: {Format(unrealized)} | มูลค่าที่ลงทุน: {Format(exposure)}",
                $"ชนะ: {results.Count(result => result > 0m)} | แพ้: {results.Count(result => result.HasValue && result <= 0m)} | ออเดอร์เปิด: {Format(openOrders)}",
                $"ตำแหน่ง: {Format(positions)} | รอสัญญาณ: {waits}",
                "กำไรขาดทุนรวมทั้งบัญชี: Unknown (รายงานเฉพาะ fills ของกลยุทธ์)",
                $"ข้อผิดพลาด: {reportFault}",
            }
        );
        var fingerprint = Convert
            .ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(
                            new
                            {
                                date,
                                mode,
                                fills,
                                text,
                            }
                        )
                    )
                )
            )
            .ToLowerInvariant();
        return new TradingDailyReport(
            date,
            mode,
            revision,
            now,
            text,
            "prepared",
            starting,
            ending,
            knownFunding ? realizedNet : null,
            fingerprint,
            ClosedAt: now >= end ? previous?.ClosedAt ?? now : null,
            EvidenceHash: evidenceHash,
            StartingEvidenceHash: startingEvidenceHash,
            OperatingMode: recordedMode
        );
    }

    private static string Hash(IReadOnlyList<TradingFill> fills) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fills))))
            .ToLowerInvariant();

    private static string Format(decimal? value, bool known = true) =>
        value.HasValue && known ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "Unknown";
}
