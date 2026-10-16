using System.Globalization;
using System.Net;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public static class TelegramOrderMessage
{
    public static string Format(OrderProposal proposal, string status)
    {
        var awaiting = proposal.State == OrderState.Pending && status == "⏳ Awaiting your approval";
        var symbol = WebUtility.HtmlEncode(proposal.Symbol);
        var buy = proposal.Side == TradeSide.Buy;
        var amount = Number(proposal.Amount);
        var price = Price(proposal.LimitPrice);
        var text =
            "<b>"
            + Status(proposal.State, status, awaiting)
            + "</b>\n\n<b>"
            + (buy ? "ซื้อ " : "ขาย ")
            + symbol
            + "</b>\n"
            + (buy ? "วงเงินคำสั่งซื้อ: " + amount + " บาท" : "จำนวนที่เสนอขาย: " + amount + " " + symbol)
            + "\n"
            + (buy ? "ราคาซื้อไม่เกิน: " : "ราคาขายไม่ต่ำกว่า: ")
            + price
            + " บาท/เหรียญ\nค่าธรรมเนียมประมาณ: "
            + proposal.EstimatedFeeThb.ToString("0.00", CultureInfo.InvariantCulture)
            + " บาท ("
            + ((proposal.Sizing?.Policy.EstimatedFeeRate ?? 0.0025m) * 100m).ToString(
                "0.##",
                CultureInfo.InvariantCulture
            )
            + "%)";

        if (proposal.Sizing is { } sizing)
        {
            text += "\n" + AdaptiveSizing(sizing);
        }

        var plan = EntryPlan(proposal);
        if (plan.Length > 0)
        {
            text += "\n\n" + plan;
        }

        text +=
            "\n\nเหตุผลและรายละเอียด\n<blockquote expandable>"
            + WebUtility.HtmlEncode(proposal.Reason)
            + "\n\nสถานะจากระบบ: "
            + WebUtility.HtmlEncode(status)
            + "</blockquote>";

        if (awaiting)
        {
            text +=
                "\n\n⏳ อนุมัติก่อน "
                + proposal
                    .ExpiresAt.ToOffset(TimeSpan.FromHours(7))
                    .ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture)
                + " น. (ไทย)\nกดอนุมัติเพื่อส่งคำสั่งเงินจริงไป Bitkub";
        }
        else if (proposal.State is OrderState.Submitted or OrderState.PartiallyFilled)
        {
            text += "\n\nดูการจับคู่ล่าสุดหรือยกเลิกคำสั่งได้ที่ Bitkub";
        }
        else if (proposal.State is OrderState.Submitting or OrderState.Unknown)
        {
            text += "\n\nตรวจ Bitkub ก่อนสร้างคำสั่งซ้ำ";
        }

        return text;
    }

    private static string Status(OrderState state, string status, bool awaiting)
    {
        if (awaiting)
        {
            return "🔔 รออนุมัติคำสั่งเงินจริง";
        }

        return state switch
        {
            OrderState.Submitted => "✅ ส่งคำสั่งแล้ว · ยังไม่ยืนยันการจับคู่",
            OrderState.PartiallyFilled => "🟡 จับคู่ได้บางส่วน",
            OrderState.Filled => "✅ จับคู่ครบแล้ว",
            OrderState.Submitting => "⏳ กำลังส่งคำสั่ง · ยังไม่ยืนยันผล",
            OrderState.Unknown => "⚠ ไม่ทราบผลคำสั่ง · ยังยืนยันการส่งไม่ได้",
            OrderState.Cancelled => "✖ ยกเลิกคำสั่งแล้ว",
            OrderState.Rejected => "✖ คำสั่งถูกปฏิเสธ",
            OrderState.Expired => "⌛ หมดเวลาอนุมัติ · ไม่ได้ส่งคำสั่ง",
            _ when status == "⌛ Expired · No order submitted" => "⌛ หมดเวลาอนุมัติ · ไม่ได้ส่งคำสั่ง",
            _ when status.StartsWith("Not submitted · ", StringComparison.Ordinal) =>
                "⚠ ยังไม่ได้ส่งคำสั่ง · ตรวจรายละเอียด",
            _ when status.StartsWith("Review interrupted · ", StringComparison.Ordinal) =>
                "⚠ ยังยืนยันผลการอนุมัติไม่ได้ · ตรวจสถานะในแอป",
            _ => "⚠ บันทึกการตัดสินใจแล้ว · ตรวจสถานะในแอป",
        };
    }

    private static string EntryPlan(OrderProposal proposal)
    {
        const string targetPrefix = "Target (plan): THB ";
        const string invalidationPrefix = "Invalidation (plan): THB ";
        const string reviewPrefix = "Review by: ";
        const string reviewSuffix = ". Exit/cancel requires review; not automatic.";
        var lines = proposal.Reason.Split('\n');
        if (
            proposal.Side != TradeSide.Buy
            || lines.Length < 4
            || !lines[^3].StartsWith(targetPrefix, StringComparison.Ordinal)
            || !lines[^2].StartsWith(invalidationPrefix, StringComparison.Ordinal)
            || !lines[^1].StartsWith(reviewPrefix, StringComparison.Ordinal)
            || !lines[^1].EndsWith(reviewSuffix, StringComparison.Ordinal)
            || lines[^1].Length < reviewPrefix.Length + reviewSuffix.Length
        )
        {
            return "";
        }

        if (
            !decimal.TryParse(
                lines[^3][targetPrefix.Length..],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var target
            )
            || !decimal.TryParse(
                lines[^2][invalidationPrefix.Length..],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var invalidation
            )
            || target <= proposal.LimitPrice
            || invalidation <= 0
            || invalidation >= proposal.LimitPrice
            || !DateTimeOffset.TryParseExact(
                lines[^1][reviewPrefix.Length..^reviewSuffix.Length],
                "yyyy-MM-dd HH:mm zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var reviewAt
            )
        )
        {
            return "";
        }

        return "แผนหลังซื้อ (ยังไม่ได้ตั้งคำสั่งขาย)\nเป้าขาย: "
            + Price(target)
            + " บาท/เหรียญ\nหากลงถึง "
            + Price(invalidation)
            + " บาท ให้ทบทวนแผน\nเวลาทบทวนตามแผน: "
            + reviewAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)
            + " น. (ไทย)\nการขายต้องอนุมัติแยก · ไม่มีขาย/ตัดขาดทุนอัตโนมัติ";
    }

    private static string AdaptiveSizing(Quantara.Portfolio.Domain.Risk.RiskSizingSnapshot sizing)
    {
        var decision = sizing.Decision;
        var allocation = decision.AllocationFraction is { } fraction
            ? (fraction * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%"
            : "ไม่ทราบ";
        var downside = decision.EstimatedDownsideThb?.ToString("0.00", CultureInfo.InvariantCulture) ?? "ไม่ทราบ";
        return "📐 ขนาดจาก Adaptive Risk: ใช้ "
            + allocation
            + " ของพอร์ต · ประเมินความเสียหายถึงจุดหยุดประมาณ "
            + downside
            + " บาท\nเหตุผลจำกัดขนาด: "
            + LimitingReason(decision.Code)
            + "\n⚠ เป็นเพียงการประมาณการ; stop และการจับคู่ไม่รับประกันขาดทุนสูงสุด";
    }

    private static string LimitingReason(string code) =>
        code switch
        {
            "availableCash" => "เงินสดที่ใช้ได้",
            "portfolioExposure" => "สัดส่วนความเสี่ยงรวมของพอร์ต",
            "concentration" => "สัดส่วนเหรียญนี้ในพอร์ต",
            "riskToStop" => "ความเสี่ยงถึงจุดหยุด",
            "liquidity" => "สภาพคล่องใน order book",
            "availableAssets" => "เหรียญที่ขายได้หลังหักคำสั่งค้าง",
            _ => "เงื่อนไขความเสี่ยงของระบบ",
        };

    private static string Number(decimal value) =>
        value.ToString("#,0.############################", CultureInfo.InvariantCulture);

    private static string Price(decimal value) =>
        value.ToString("#,0.00##########################", CultureInfo.InvariantCulture);
}
