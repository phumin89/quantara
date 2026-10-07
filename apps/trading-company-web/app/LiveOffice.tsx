'use client';
import { useEffect, useRef } from "react";
import type { LiveActivity } from "./liveActivity";
import { liveActivityRoles } from "./liveActivity";

export function LiveOffice({ activity }: { activity?: LiveActivity }) {
  const frame = useRef<HTMLIFrameElement>(null);
  const publish = () => frame.current?.contentWindow?.postMessage({ type: "quantara-live-activity", activity }, "https://localhost:3000");
  useEffect(publish, [activity]);
  const working = activity?.roles.find(r => r.status === "working");
  const names: Record<string, string> = { ceo: "CEO", "chief-of-staff": "ผู้ประสานงาน", "market-research": "ทีมตลาด", "security-analysis": "ทีมวิเคราะห์เหรียญ", "quant-research": "ทีมกลยุทธ์", "portfolio-initial": "ทีมพอร์ต", "risk-critic": "ทีมความเสี่ยง", "portfolio-final": "ทีมพอร์ต" };
  const validSymbols = (value: unknown): value is string[] => Array.isArray(value) && value.length <= 3 && value.every(s => ["BTC-THB", "ETH-THB", "SOL-THB"].includes(s)) && new Set(value).size === value.length;
  const hasSymbols = validSymbols(activity?.researchSymbols) && activity!.researchSymbols!.length > 0 && (activity?.tradingSymbols === undefined || validSymbols(activity.tradingSymbols) && activity.tradingSymbols.every(s => activity.researchSymbols!.includes(s)));
  const status = working ? `${names[working.role]} กำลังวิเคราะห์` : ({ READ_ONLY: "เฝ้าตลาด · พักส่งคำสั่ง", RESEARCH_UNAVAILABLE: "รอข้อมูลตลาด", LIVE_BLOCKED: "รอข้อมูลบัญชี", LIVE_UNAVAILABLE: "เชื่อมระบบอีกครั้ง", WATCHING: "เฝ้าตลาด", ALREADY_ATTEMPTED: "รอรอบทบทวนถัดไป", BEFORE_MEETING: "รอประชุม 09:00", ORDER_PENDING: "รอผลคำสั่งจากตลาด", HOLD: "คงพอร์ต" }[activity?.coordinatorStatus ?? ""] ?? (activity?.coordinatorStatus ? "รอข้อมูล" : ""));
  const team = activity ? `ทีม ${liveActivityRoles.length} บทบาท${working ? " · กำลังทำงาน 1" : ""}` : "";
  const coins = hasSymbols ? `วิเคราะห์ ${activity!.researchSymbols!.map(s => s.slice(0, -4)).join(" · ")}` : "";
  const trading = hasSymbols && activity?.tradingSymbols?.length && (activity.tradingSymbols.length !== activity.researchSymbols!.length || activity.tradingSymbols.some((s, i) => s !== activity.researchSymbols![i])) ? ` · เทรดได้ ${activity.tradingSymbols.map(s => s.slice(0, -4)).join(" · ")}` : "";
  const issueText: Record<string, string> = { MARKET_HISTORY_UNAVAILABLE: "ข้อมูลประวัติตลาดไม่พร้อม", PUBLIC_RESEARCH_UNAVAILABLE: "ข้อมูลวิจัยสาธารณะไม่พร้อม", LIVE_READ_UNAVAILABLE: "อ่านสถานะ Live ไม่ได้", ACCOUNT_STATE_INVALID: "สถานะบัญชีไม่ถูกต้อง", JOURNAL_UNAVAILABLE: "บันทึกการประชุมไม่พร้อม", MEETING_FAILED: "การประชุมรอบนี้ไม่สำเร็จ" };
  const issues = activity?.issues?.map(i => `${issueText[i.code]}${i.symbol ? ` (${i.symbol})` : ""}`).join(" · ");
  const retry = activity?.nextRetryAt ? new Intl.DateTimeFormat("th-TH", { timeZone: "Asia/Bangkok", dateStyle: "medium", timeStyle: "short" }).format(new Date(activity.nextRetryAt)) : "";
  const savedIssues = working ? "" : issues;
  const savedRetry = working ? "" : retry;
  const detail = [team, coins].filter(Boolean).join(" · ");
  return <section className="live-office" aria-label="สำนักงาน Pixel ของ Quantara">
    <div className="live-office-status">
      <p className="live-office-activity" role="status">{status || "ยังยืนยันสถานะทีมล่าสุดไม่ได้"}</p>
      <details className="live-office-details"><summary>รายละเอียดการทำงาน</summary>
        {detail && <p>{detail}{trading}</p>}
        {savedIssues && <p>{savedIssues}</p>}
        {savedRetry && <p>ตรวจอีกครั้ง {savedRetry}</p>}
        {!activity && <p>ยังอ่านสถานะทีมไม่ได้ การขาดข้อมูลไม่ได้ยืนยันว่าทีมหยุดทำงาน</p>}
      </details>
    </div>
    <iframe ref={frame} onLoad={publish} className="live-office-frame" title="สำนักงาน Pixel ของ Quantara" src="/office/live.html" sandbox="allow-scripts allow-same-origin" />
  </section>;
}
