using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingAuthority
{
    public static bool Valid(TradingState state, string accountKey, DateTimeOffset now) =>
        state.Mode == TradingMode.AutoLive
        && !state.EmergencyStopped
        && state.Mandate is { RevokedAt: null } mandate
        && mandate.AccountKey == accountKey
        && mandate.RulesHash == state.RulesHash
        && state.RulesHash == TradingRuleHash.Compute(state.Rules)
        && mandate.CreatedAt <= now
        && now < mandate.ExpiresAt;

    public static TradingState Fault(TradingState state, string kind, DateTimeOffset now)
    {
        if (state.Fault == kind)
        {
            return state;
        }

        if (state.Alerts.Any(alert => alert.Kind == kind && now - alert.CreatedAt < TimeSpan.FromMinutes(15)))
        {
            return state with { Fault = kind };
        }

        var alert = new TradingAlert(
            Guid.NewGuid().ToString("N"),
            kind,
            "⚠️ ระบบเทรด: " + Describe(kind) + "\nหยุดเปิดสถานะใหม่จนกว่าจะตรวจสอบได้",
            now
        );
        return state with { Fault = kind, Alerts = state.Alerts.Append(alert).ToArray() };
    }

    public static string Describe(string kind) =>
        kind switch
        {
            "Stale or unavailable market feed" => "ข้อมูลตลาดไม่สดหรือไม่พร้อมใช้งาน",
            "Uncertain broker order — reconciliation required" =>
                "ยังยืนยันผลคำสั่งกับ Bitkub ไม่ได้ ต้องตรวจสอบก่อนทำต่อ",
            "Daily loss limit reached" => "ถึงขีดจำกัดขาดทุนรายวันแล้ว",
            "Mandate expired or invalid" => "สิทธิ์เทรดหมดอายุหรือไม่ตรงกับกฎปัจจุบัน",
            "Recovered after prolonged trading outage" => "ระบบกลับมาหลังหยุดเกิน 3 นาที ไม่ยืนยันการเทรดระหว่างหยุด",
            "Trading dependency or reconciliation failure — new entries paused" =>
                "ข้อมูลหรือการตรวจสอบคำสั่งไม่พร้อมใช้งาน",
            "Emergency stop — open broker orders remain subject to reconciliation" =>
                "เจ้าของหยุดฉุกเฉิน คำสั่งที่เปิดอยู่ยังต้องตรวจสอบกับ Bitkub",
            _ => kind,
        };
}
