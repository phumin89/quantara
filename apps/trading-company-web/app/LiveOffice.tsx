'use client';
import { useEffect, useRef } from "react";
import type { LiveActivity } from "./liveActivity";

export function LiveOffice({ activity }: { activity?: LiveActivity }) {
  const frame = useRef<HTMLIFrameElement>(null);
  const publish = () => frame.current?.contentWindow?.postMessage({ type: "quantara-live-activity", activity }, "https://localhost:3000");
  useEffect(publish, [activity]);
  const working = activity?.roles.find(r => r.status === "working");
  const names: Record<string, string> = { ceo: "CEO", "chief-of-staff": "ผู้ประสานงาน", "market-research": "ทีมตลาด", "security-analysis": "ทีมวิเคราะห์เหรียญ", "quant-research": "ทีมกลยุทธ์", "portfolio-initial": "ทีมพอร์ต", "risk-critic": "ทีมความเสี่ยง", "portfolio-final": "ทีมพอร์ต" };
  return <section className="live-office" aria-label="สำนักงาน Pixel ของ Quantara">
    {working && <p className="live-office-activity" role="status">{names[working.role]}กำลังวิเคราะห์</p>}
    <iframe ref={frame} onLoad={publish} className="live-office-frame" title="สำนักงาน Pixel ของ Quantara" src="/office/live.html" sandbox="allow-scripts allow-same-origin" />
  </section>;
}
