'use client';
import { useEffect, useRef } from "react";
import type { LiveActivity } from "./liveActivity";

export function LiveOffice({ activity }: { activity?: LiveActivity }) {
  const frame = useRef<HTMLIFrameElement>(null);
  const publish = () => frame.current?.contentWindow?.postMessage({ type: "quantara-live-activity", activity }, "https://localhost:3000");
  useEffect(publish, [activity]);
  const working = activity?.roles.find(r => r.status === "working");
  const names: Record<string, string> = { ceo: "CEO", "chief-of-staff": "ผู้ประสานงาน", "market-research": "ทีมตลาด", "security-analysis": "ทีมวิเคราะห์เหรียญ", "quant-research": "ทีมกลยุทธ์", "portfolio-initial": "ทีมพอร์ต", "risk-critic": "ทีมความเสี่ยง", "portfolio-final": "ทีมพอร์ต" };
  const validSymbols = (value: unknown): value is string[] => Array.isArray(value) && value.length <= 3 && value.every(s => ["BTC-THB", "ETH-THB", "SOL-THB"].includes(s)) && new Set(value).size === value.length;
  const hasSymbols = validSymbols(activity?.researchSymbols) && activity!.researchSymbols!.length > 0 && (activity?.tradingSymbols === undefined || validSymbols(activity.tradingSymbols) && activity.tradingSymbols.every(s => activity.researchSymbols!.includes(s)));
  const status = working ? `${names[working.role]}กำลังวิเคราะห์` : ({ WATCHING: "เฝ้าตลาด", ALREADY_ATTEMPTED: "รอรอบทบทวนถัดไป", BEFORE_MEETING: "รอประชุม 09:00", ORDER_PENDING: "รอผลคำสั่งจากตลาด", LIVE_BLOCKED: "พักการวิเคราะห์ · ตรวจสถานะบัญชี", HOLD: "คงพอร์ต" }[activity?.coordinatorStatus ?? ""] ?? (activity?.coordinatorStatus ? "รอข้อมูล" : ""));
  const coins = hasSymbols ? `วิเคราะห์ ${activity!.researchSymbols!.map(s => s.slice(0, -4)).join(" · ")}` : "";
  const trading = hasSymbols && activity?.tradingSymbols?.length && (activity.tradingSymbols.length !== activity.researchSymbols!.length || activity.tradingSymbols.some((s, i) => s !== activity.researchSymbols![i])) ? ` · เทรดได้ ${activity.tradingSymbols.map(s => s.slice(0, -4)).join(" · ")}` : "";
  return <section className="live-office" aria-label="สำนักงาน Pixel ของ Quantara">
    {(status || coins) && <p className="live-office-activity" role="status">{status}{status && coins ? " · " : ""}{coins}{trading}</p>}
    <iframe ref={frame} onLoad={publish} className="live-office-frame" title="สำนักงาน Pixel ของ Quantara" src="/office/live.html" sandbox="allow-scripts allow-same-origin" />
  </section>;
}
