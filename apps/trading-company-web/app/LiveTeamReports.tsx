"use client";
import { useEffect, useState } from "react";
import { requestJson } from "./requestJson";
import { CoinResearchView, readResearch } from "./CoinResearchView";
const roles: Record<string, string> = { ceo: "CEO", "chief-of-staff": "หัวหน้าทีม", "market-research": "วิจัยตลาด", "security-analysis": "วิเคราะห์สินทรัพย์", "quant-research": "วิเคราะห์กลยุทธ์", "portfolio-initial": "ข้อเสนอแรก", "risk-critic": "ตรวจความเสี่ยง", "portfolio-final": "ผู้จัดการพอร์ต" };
const statuses: Record<string, string> = { HOLD: "คงพอร์ต · ยังไม่ส่งคำสั่ง", ORDER_PENDING: "ส่งคำสั่งแล้ว · ยังรอยืนยันผลจากตลาด", RISK_REJECTED: "ระบบไม่อนุมัติคำสั่ง", RESEARCH_UNAVAILABLE: "ข้อมูลตลาดไม่ครบ จึงไม่มีคำสั่ง", SNAPSHOT_CHANGED: "บัญชีหรือสิทธิ์เปลี่ยนระหว่างประชุม จึงไม่มีคำสั่ง", PRICE_CHANGED: "ราคาเปลี่ยนเกินขอบเขต จึงไม่มีคำสั่ง", LIVE_UNAVAILABLE: "ยังยืนยันผลรอบนี้ไม่ได้ ตรวจรายการคำสั่งก่อนดำเนินการต่อ" };
type Scope = { mode: 1; accountId: string };
type Report = { role: string; summary: string; providerModel: string; inputTokens: number; outputTokens: number; artifactId: string; evidenceHash: string; dataCutoff: string };
type Meeting = { businessDate: string; reviewHour?: number | null; attemptId: string; status: string; accountContext: Scope; summary: { modelCalls: number; inputTokens: number; outputTokens: number; reports: Report[]; research?: unknown } | null };
type Meetings = { accountContext: Scope; meetings: Meeting[] };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === "object" && !Array.isArray(v);
const text = (v: unknown, max: number): v is string => typeof v === "string" && v.length > 0 && v.length <= max;
const count = (v: unknown): v is number => typeof v === "number" && Number.isSafeInteger(v) && v >= 0 && v <= 10_000_000;
const uuid = (v: unknown) => typeof v === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v);
const date = (v: unknown): v is string => typeof v === "string" && /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v)) && new Date(v).toISOString().startsWith(v);
export function readMeetings(value: unknown, now = Date.now()): Meetings | undefined {
  if (!object(value) || !object(value.accountContext)) return;
  const scope = value.accountContext;
  if (scope.mode !== 1 || typeof scope.accountId !== "string" || !/^[0-9a-f]{32}$/i.test(scope.accountId) || /^0{32}$/.test(scope.accountId) || !Array.isArray(value.meetings) || value.meetings.length > 31) return;
  const slots = new Set(), ids = new Set();
  for (const m of value.meetings) {
    const hour = m && object(m) ? m.reviewHour : undefined;
    if (!object(m) || !date(m.businessDate) || !uuid(m.attemptId) || (hour !== undefined && hour !== null && (typeof hour !== "number" || !Number.isInteger(hour) || hour < 0 || hour > 23)) || slots.has(`${m.businessDate}:${hour ?? "daily"}`) || ids.has(m.attemptId) ||
        !text(m.status, 100) || !/^[A-Z_]+$/.test(m.status) || m.status === "STARTED" || !object(m.accountContext) ||
        m.accountContext.mode !== scope.mode || m.accountContext.accountId !== scope.accountId) return;
    slots.add(`${m.businessDate}:${hour ?? "daily"}`); ids.add(m.attemptId);
    if (m.summary == null) continue;
    const s = m.summary;
    if (!object(s) || !count(s.modelCalls) || s.modelCalls > 8 || !count(s.inputTokens) || !count(s.outputTokens) || !Array.isArray(s.reports) || s.reports.length > 8) return;
    if (s.research != null && (!readResearch(s.research, now) || new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Bangkok" }).format(new Date((s.research as Record<string, unknown>).observedAt as string)) !== m.businessDate)) return;
    const seen = new Set();
    for (const r of s.reports) {
      if (!object(r) || !text(r.role, 100) || !Object.hasOwn(roles, r.role) || seen.has(r.role) || !text(r.summary, 6000) ||
          !text(r.providerModel, 200) || !count(r.inputTokens) || !count(r.outputTokens) || !uuid(r.artifactId) ||
          typeof r.evidenceHash !== "string" || !/^[0-9a-f]{64}$/i.test(r.evidenceHash) || !date(r.dataCutoff) || r.dataCutoff > m.businessDate) return;
      seen.add(r.role);
    }
  }
  return { accountContext: scope as Scope, meetings: (value.meetings as Meeting[]).slice().sort((a, b) => b.businessDate.localeCompare(a.businessDate) || (b.reviewHour ?? 9) - (a.reviewHour ?? 9)) };
}
export async function readLiveMeetings(origin: string, fetcher: typeof fetch = fetch, signal?: AbortSignal) {
  if (origin !== "https://localhost:3000") return;
  const response = await requestJson("https://localhost:7443/live/meetings", { credentials: "include", signal }, fetcher);
  return response.status === 200 ? readMeetings(response.data) : undefined;
}
export function LiveTeamReports({ enabled }: { enabled: boolean }) {
  const [data, setData] = useState<Meetings>();
  const [bad, setBad] = useState(false);
  const [selected, setSelected] = useState("");
  useEffect(() => {
    setData(undefined); setBad(false); setSelected("");
    if (!enabled) return;
    const controller = new AbortController(); let timer: ReturnType<typeof setTimeout>;
    async function load() {
      try { const next = await readLiveMeetings(window.location.origin, fetch, controller.signal); if (!controller.signal.aborted) { setData(next); setBad(!next); } }
      catch { if (!controller.signal.aborted) { setData(undefined); setBad(true); } }
      finally { if (!controller.signal.aborted) timer = setTimeout(load, 30000); }
    }
    void load(); return () => { controller.abort(); clearTimeout(timer); };
  }, [enabled]);
  if (!enabled) return null;
  return <section className="team-report-state"><h3>รายงานประชุม Live</h3>
    {bad ? <p>ยังอ่านรายงานที่บันทึกไว้ไม่ได้ ข้อมูลขาดไม่ได้แปลว่าไม่มีการทำงาน</p> : !data ? <p role="status">กำลังอ่านรายงานที่บันทึกไว้…</p> : !data.meetings.length ? <p>ยังไม่มีรอบ Live ที่บันทึกไว้ การจับคู่หรือบันทึกวงเงินยังไม่เริ่มประชุม</p> :
      <LiveMeetingView meetings={data.meetings} selected={selected} onSelect={setSelected} />}
  </section>;
}
export function LiveMeetingView({ meetings, selected, onSelect }: { meetings: Meeting[]; selected: string; onSelect: (value: string) => void }) {
  const meeting = meetings.find(m => m.attemptId === selected) ?? meetings.find(m => m.businessDate === selected) ?? meetings[0];
  const reports = meeting.summary?.reports ?? [], final = reports.find(r => r.role === "portfolio-final"), initial = reports.find(r => r.role === "portfolio-initial");
  return <><label className="team-meeting-date">รอบประชุม<select value={meeting.attemptId} onChange={e => onSelect(e.target.value)}>{meetings.map(m => <option key={m.attemptId} value={m.attemptId}>{new Date(m.businessDate + "T00:00:00Z").toLocaleDateString("th-TH", { timeZone: "Asia/Bangkok", dateStyle: "medium" })} · {m.reviewHour == null ? "ประชุมประจำวัน" : `ทบทวน ${String(m.reviewHour).padStart(2, "0")}:00`}</option>)}</select></label>
    <p className="team-meeting-outcome">{statuses[meeting.status] ?? "รอบสิ้นสุดโดยยังไม่มีข้อเสนอที่นำไปใช้ได้"}</p>
    {final && <p className="team-meeting-conclusion">{final.summary}</p>}
    {meeting.summary?.research != null && <CoinResearchView research={meeting.summary.research} businessDate={meeting.businessDate} />}
    {!!reports.length && <details><summary>อ่านเหตุผลจากทีม</summary>{reports.filter(r => !["portfolio-final", "portfolio-initial"].includes(r.role)).map(r => <article className="team-role-report" key={r.role}><h4>{roles[r.role]}</h4><p>{r.summary}</p></article>)}
      {initial && <details><summary>ข้อเสนอแรกก่อนตรวจความเสี่ยง</summary><p>{initial.summary}</p></details>}
    </details>}
    {!reports.length && <p>รอบนี้ไม่มีรายงานจากทีมที่ผ่านการตรวจและบันทึกไว้</p>}
  </>;
}
