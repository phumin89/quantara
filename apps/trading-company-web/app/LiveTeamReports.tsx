"use client";
import { useEffect, useRef, useState } from "react";
import { requestJson } from "./requestJson";
import { CoinResearchView, readResearch } from "./CoinResearchView";
import { CompanyLearningView, readLearning } from "./CompanyLearningView";
import { validLiveIssues } from "./liveActivity";
const roles: Record<string, string> = { ceo: "CEO", "chief-of-staff": "หัวหน้าทีม", "market-research": "วิจัยตลาด", "security-analysis": "วิเคราะห์สินทรัพย์", "quant-research": "วิเคราะห์กลยุทธ์", "portfolio-initial": "ข้อเสนอแรก", "risk-critic": "ตรวจความเสี่ยง", "portfolio-final": "ผู้จัดการพอร์ต" };
const statuses: Record<string, string> = { PLAN_REGISTERED: "บันทึกแผนแล้ว · ระบบเฝ้าราคาเข้า–ออก", PLAN_REJECTED: "แผนยังไม่ผ่านเงื่อนไข", READ_ONLY: "รอบนี้วิเคราะห์เท่านั้น · ไม่ได้ส่งคำสั่ง", HOLD: "คงพอร์ต · ยังไม่ส่งคำสั่ง", ORDER_PENDING: "ส่งคำสั่งแล้ว · ยังรอยืนยันผลจากตลาด", RISK_REJECTED: "ระบบไม่อนุมัติคำสั่ง", RESEARCH_UNAVAILABLE: "ข้อมูลตลาดไม่ครบ จึงไม่มีคำสั่ง", SNAPSHOT_CHANGED: "บัญชีหรือสิทธิ์เปลี่ยนระหว่างประชุม จึงไม่มีคำสั่ง", PRICE_CHANGED: "ราคาเปลี่ยนเกินขอบเขต จึงไม่มีคำสั่ง", LIVE_UNAVAILABLE: "ยังยืนยันผลรอบนี้ไม่ได้ ตรวจรายการคำสั่งก่อนดำเนินการต่อ" };
type Scope = { mode: 1; accountId: string };
type Report = { role: string; summary: string; providerModel: string; inputTokens: number; outputTokens: number; artifactId: string; evidenceHash: string; dataCutoff: string };
type Meeting = { businessDate: string; reviewHour?: number | null; reviewMinute?: number | null; attemptId: string; status: string; accountContext: Scope; summary: { modelCalls: number; inputTokens: number; outputTokens: number; reports: Report[]; research?: unknown; learning?: unknown; issues?: { code: string; symbol?: string | null }[] | null; advisoryOnly?: boolean; planRejectionReasons?: string[] | null } | null };
type Meetings = { accountContext: Scope; meetings: Meeting[]; availableDates: string[]; selectedDate: string | null; hasDateIndex: boolean };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === "object" && !Array.isArray(v);
const text = (v: unknown, max: number): v is string => typeof v === "string" && v.length > 0 && v.length <= max;
const count = (v: unknown): v is number => typeof v === "number" && Number.isSafeInteger(v) && v >= 0 && v <= 10_000_000;
const uuid = (v: unknown) => typeof v === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v);
const date = (v: unknown): v is string => typeof v === "string" && /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v)) && new Date(v).toISOString().startsWith(v);
function ReportPlaceholder({ message, failed = false, onRetry }: { message: string; failed?: boolean; onRetry?: () => void }) {
  return <div className="report-skeleton" role={failed ? "alert" : "status"} aria-label={message}><p>{message}</p>{failed ? onRetry && <button className="team-report-retry" type="button" onClick={onRetry}>ลองใหม่</button> : <><span className="skeleton-line" /><span className="skeleton-line" /><span className="skeleton-line" /></>}</div>;
}
export function readMeetings(value: unknown, now = Date.now()): Meetings | undefined {
  if (!object(value) || !object(value.accountContext)) return;
  const scope = value.accountContext;
  if (scope.mode !== 1 || typeof scope.accountId !== "string" || !/^[0-9a-f]{32}$/i.test(scope.accountId) || /^0{32}$/.test(scope.accountId) || !Array.isArray(value.meetings) || value.meetings.length > 97) return;
  const slots = new Set(), ids = new Set();
  for (const m of value.meetings) {
    const hour = m && object(m) ? m.reviewHour : undefined;
    const minute = m && object(m) ? m.reviewMinute : undefined;
    if (!object(m) || !date(m.businessDate) || !uuid(m.attemptId) || (hour !== undefined && hour !== null && (typeof hour !== "number" || !Number.isInteger(hour) || hour < 0 || hour > 23)) || (minute !== undefined && minute !== null && (typeof minute !== "number" || !Number.isInteger(minute) || ![0, 15, 30, 45].includes(minute))) || (minute != null && minute !== 0 && (hour == null || typeof hour !== "number" || !Number.isInteger(hour) || hour < 0 || hour > 23)) || slots.has(`${m.businessDate}:${hour ?? "daily"}:${minute ?? 0}`) || ids.has(m.attemptId) ||
        !text(m.status, 100) || !/^[A-Z_]+$/.test(m.status) || m.status === "STARTED" || !object(m.accountContext) ||
        m.accountContext.mode !== scope.mode || m.accountContext.accountId !== scope.accountId) return;
    slots.add(`${m.businessDate}:${hour ?? "daily"}:${minute ?? 0}`); ids.add(m.attemptId);
    if (m.summary == null) continue;
    const s = m.summary;
    if (!object(s) || !count(s.modelCalls) || s.modelCalls > 8 || !count(s.inputTokens) || !count(s.outputTokens) || !Array.isArray(s.reports) || s.reports.length > 8 || (s.issues !== undefined && s.issues !== null && !validLiveIssues(s.issues)) || (s.advisoryOnly !== undefined && typeof s.advisoryOnly !== "boolean")) return;
    if (s.research != null && (!readResearch(s.research, now) || new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Bangkok" }).format(new Date((s.research as Record<string, unknown>).observedAt as string)) !== m.businessDate)) return;
    if (s.learning != null && !readLearning(s.learning, scope as Scope, m.businessDate)) return;
    if (s.planRejectionReasons != null && (!Array.isArray(s.planRejectionReasons) || s.planRejectionReasons.length < 1 || s.planRejectionReasons.length > 20 || s.planRejectionReasons.some(r => !text(r, 100) || !/^[A-Z_]+$/.test(r)) || new Set(s.planRejectionReasons).size !== s.planRejectionReasons.length)) return;
    const seen = new Set();
    for (const r of s.reports) {
      if (!object(r) || !text(r.role, 100) || !Object.hasOwn(roles, r.role) || seen.has(r.role) || !text(r.summary, 6000) ||
          !text(r.providerModel, 200) || !count(r.inputTokens) || !count(r.outputTokens) || !uuid(r.artifactId) ||
          typeof r.evidenceHash !== "string" || !/^[0-9a-f]{64}$/i.test(r.evidenceHash) || !date(r.dataCutoff) || r.dataCutoff > m.businessDate) return;
      seen.add(r.role);
    }
  }
  const meetings = (value.meetings as Meeting[]).slice().sort((a, b) => b.businessDate.localeCompare(a.businessDate) || (b.reviewHour ?? 9) - (a.reviewHour ?? 9) || (b.reviewMinute ?? 0) - (a.reviewMinute ?? 0));
  const derived = Array.from(new Set(meetings.map(m => m.businessDate))).sort().reverse();
  if (value.availableDates !== undefined && (!Array.isArray(value.availableDates) || value.availableDates.length > 10000 || value.availableDates.some(d => !date(d)) || new Set(value.availableDates).size !== value.availableDates.length || [...value.availableDates].sort().reverse().join() !== [...value.availableDates].join())) return;
  const availableDates = (value.availableDates as string[] | undefined) ?? derived;
  if (meetings.some(m => !availableDates.includes(m.businessDate))) return;
  if (value.selectedDate !== undefined && value.selectedDate !== null && (!date(value.selectedDate) || meetings.some(m => m.businessDate !== value.selectedDate) ||
      (meetings.length === 0 && availableDates.includes(value.selectedDate)))) return;
  if (value.selectedDate == null && availableDates.length > 0 && meetings.length === 0) return;
  return { accountContext: scope as Scope, meetings, availableDates, selectedDate: value.selectedDate === undefined ? null : value.selectedDate as string | null,
    hasDateIndex: value.availableDates !== undefined };
}
export async function readLiveMeetings(origin: string, fetcher: typeof fetch = fetch, signal?: AbortSignal, requestedDate?: string) {
  if (origin !== "https://localhost:3000") return;
  if (requestedDate !== undefined && !date(requestedDate)) return;
  const endpoint = requestedDate ? `https://localhost:7443/live/meetings?date=${encodeURIComponent(requestedDate)}` : "https://localhost:7443/live/meetings";
  const response = await requestJson(endpoint, { credentials: "include", signal }, fetcher);
  if (response.status !== 200) return;
  const parsed = readMeetings(response.data);
  if (!parsed || (requestedDate !== undefined && parsed.selectedDate !== requestedDate)) return;
  return parsed;
}
export function LiveTeamReports({ enabled }: { enabled: boolean }) {
  const [data, setData] = useState<Meetings>();
  const [bad, setBad] = useState(false);
  const [selected, setSelected] = useState("");
  const [selectedDate, setSelectedDate] = useState<string | undefined>();
  const [pending, setPending] = useState(false);
  const [retry, setRetry] = useState(0);
  const current = useRef<Meetings | undefined>(undefined);
  useEffect(() => {
    current.current = undefined;
    setData(undefined); setBad(false); setSelected(""); setSelectedDate(undefined);
  }, [enabled]);
  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController(); let timer: ReturnType<typeof setTimeout>;
    const chosenDate = selectedDate;
    const emptyDate = current.current?.hasDateIndex && chosenDate && !current.current.availableDates.includes(chosenDate);
    setPending(!emptyDate); setBad(false);
    async function load() {
      const day = selectedDate;
      const requested = day && current.current?.hasDateIndex && current.current.availableDates.includes(day) ? day : undefined;
      try {
        const next = await readLiveMeetings(window.location.origin, fetch, controller.signal, requested);
        if (!controller.signal.aborted) {
          if (next) { current.current = next; setData(next); }
          setBad(!next);
        }
      }
      catch { if (!controller.signal.aborted) setBad(true); }
      finally { if (!controller.signal.aborted) { setPending(false); timer = setTimeout(load, 30000); } }
    }
    if (emptyDate) timer = setTimeout(load, 30000); else void load();
    return () => { controller.abort(); clearTimeout(timer); };
  }, [enabled, selectedDate, retry]);
  if (!enabled) return null;
  const day = selectedDate ?? data?.availableDates[0];
  const dayLoaded = data?.selectedDate === day || (data?.selectedDate === null && data.availableDates[0] === day);
  return <section className="team-report-state"><h3>รายงานประชุม Live</h3>
    {!data ? <ReportPlaceholder message={bad ? "ยังอ่านรายงานไม่ได้" : "กำลังอ่านรายงาน…"} failed={bad} onRetry={() => setRetry(value => value + 1)} /> :
      <LiveMeetingView meetings={data.meetings} selected={selected} onSelect={setSelected} availableDates={data.availableDates}
        selectedDate={day} pending={pending || (!!day && data.hasDateIndex && data.availableDates.includes(day) && !dayLoaded)}
        failed={bad || (!!selectedDate && !data.hasDateIndex && !data.availableDates.includes(selectedDate))}
        onRetry={() => setRetry(value => value + 1)}
        onDateSelect={day => { if (date(day)) { setSelectedDate(day); setSelected(""); } }} />}
  </section>;
}
export function LiveMeetingView({ meetings, selected, onSelect, availableDates, selectedDate, onDateSelect, onRetry, pending = false, failed = false }: {
  meetings: Meeting[]; selected: string; onSelect: (value: string) => void; availableDates?: string[]; selectedDate?: string;
  onDateSelect?: (value: string) => void; onRetry?: () => void; pending?: boolean; failed?: boolean;
}) {
  availableDates ??= Array.from(new Set(meetings.map(m => m.businessDate))).sort().reverse();
  const chosenDate = selectedDate ?? availableDates[0] ?? meetings[0]?.businessDate;
  const dayMeetings = chosenDate ? meetings.filter(m => m.businessDate === chosenDate) : meetings;
  const meeting = dayMeetings.find(m => m.attemptId === selected) ?? dayMeetings[0];
  const picker = <DatePicker availableDates={availableDates} selectedDate={chosenDate} onDateSelect={onDateSelect} />;
  if (failed && !meeting) return <>{picker}<ReportPlaceholder message="ยังอ่านรายงานวันที่เลือกไม่ได้" failed onRetry={onRetry} /> </>;
  if (pending && !meeting) return <>{picker}<ReportPlaceholder message="กำลังอ่านรายงาน…" /></>;
  if (!meeting) return <>{picker}<p>{availableDates.length ? "ยังไม่มีรายงานในวันที่เลือก" : "ยังไม่มีรายงานประชุม"}</p></>;
  const reports = meeting.summary?.reports ?? [], final = reports.find(r => r.role === "portfolio-final"), initial = reports.find(r => r.role === "portfolio-initial");
  const issueText: Record<string, string> = { MARKET_HISTORY_UNAVAILABLE: "กราฟประวัติตลาดของรอบนี้ไม่ครบ", PUBLIC_RESEARCH_UNAVAILABLE: "ข้อมูลวิจัยสาธารณะไม่พร้อม", LIVE_READ_UNAVAILABLE: "อ่านสถานะ Live ไม่ได้", ACCOUNT_STATE_INVALID: "สถานะบัญชีไม่ถูกต้อง", JOURNAL_UNAVAILABLE: "บันทึกการประชุมไม่พร้อม", MEETING_FAILED: "การประชุมรอบนี้ไม่สำเร็จ" };
  const historyGaps = meeting.summary?.issues?.filter(i => i.code === "MARKET_HISTORY_UNAVAILABLE");
  const issues = meeting.summary?.issues?.filter(i => i.code !== "MARKET_HISTORY_UNAVAILABLE").map(i => issueText[i.code]);
  if (historyGaps?.length) issues?.unshift(`${issueText.MARKET_HISTORY_UNAVAILABLE} ${historyGaps.map(i => `(${i.symbol})`).join(" · ")}`);
  const meetingLabel = meeting.reviewHour == null ? "ประชุมประจำวัน" : `ทบทวน ${String(meeting.reviewHour).padStart(2, "0")}:${String(meeting.reviewMinute ?? 0).padStart(2, "0")}`;
  return <><DatePicker availableDates={availableDates} selectedDate={chosenDate} onDateSelect={onDateSelect} /><p className="report-update-state" role={failed ? "alert" : "status"}>{failed ? "อัปเดตรายงานไม่สำเร็จ กำลังใช้รายงานที่บันทึกไว้" : pending ? "กำลังอัปเดตรายงาน · แสดงรายงานที่บันทึกไว้" : ""}</p>
    {failed && onRetry && <button className="team-report-retry" type="button" onClick={onRetry}>ลองใหม่</button>}
    {dayMeetings.length > 1 ? <label className="team-meeting-date">รอบประชุม<select value={meeting.attemptId} onChange={e => onSelect(e.target.value)}>{dayMeetings.map(m => <option key={m.attemptId} value={m.attemptId}>{m.reviewHour == null ? "ประชุมประจำวัน" : `ทบทวน ${String(m.reviewHour).padStart(2, "0")}:${String(m.reviewMinute ?? 0).padStart(2, "0")}`}</option>)}</select></label> : <p className="team-meeting-date-label">รอบประชุม · {meetingLabel}</p>}
    <p className="team-meeting-outcome">{meeting.summary?.advisoryOnly ? "รอบนี้วิเคราะห์เท่านั้น · ไม่ได้ส่งคำสั่ง" : statuses[meeting.status] ?? "รอบสิ้นสุดโดยยังไม่มีข้อเสนอที่นำไปใช้ได้"}</p>
    {!!issues?.length && <p className="team-meeting-issues">{issues.join(" · ")}</p>}
    {!!meeting.summary?.planRejectionReasons?.length && <p className="team-meeting-issues">{Array.from(new Set(meeting.summary.planRejectionReasons.map(r => ({ PLAN_ALREADY_ACTIVE: "มีแผนเหรียญนี้อยู่แล้ว", PLAN_ROUND_TRIP_TURNOVER_EXCEEDED: "วงเงินวันนี้ไม่พอทั้งซื้อและขาย", PLAN_EXIT_BELOW_MINIMUM: "จำนวนขายต่ำกว่าขั้นต่ำตลาด", PLAN_TARGET_BELOW_COSTS: "เป้าขายยังไม่คุ้มค่าธรรมเนียม", PLAN_CASH_OVERCOMMITTED: "เงินสดไม่พอรวมแผนที่รออยู่", PLAN_EXIT_ORDER_LIMIT: "ยอดขายเกินวงเงินต่อคำสั่ง", UNRESOLVED_ORDER: "ยังมีคำสั่งรอยืนยัน", OPEN_POSITION_LIMIT: "จำนวนเหรียญเกินสิทธิ์ที่เปิดไว้", NOT_ARMED: "ยังไม่เปิดสิทธิ์ส่งคำสั่ง", STOPPED: "เจ้าของพักส่งคำสั่งแล้ว" } as Record<string, string>)[r] ?? "แผนไม่ผ่านการตรวจบัญชีหรือความเสี่ยง"))).join(" · ")}</p>}
    {final && <p className="team-meeting-conclusion">{final.summary}</p>}
    {meeting.summary?.research != null && <CoinResearchView research={meeting.summary.research} businessDate={meeting.businessDate} />}
    {meeting.summary?.learning != null && <CompanyLearningView learning={meeting.summary.learning} scope={meeting.accountContext} meetingDate={meeting.businessDate} labels={roles} />}
    {!!reports.length && <details><summary>อ่านเหตุผลจากทีม</summary>{reports.filter(r => !["portfolio-final", "portfolio-initial"].includes(r.role)).map(r => <article className="team-role-report" key={r.role}><h4>{roles[r.role]}</h4><p>{r.summary}</p></article>)}
      {initial && <details><summary>ข้อเสนอแรกก่อนตรวจความเสี่ยง</summary><p>{initial.summary}</p></details>}
    </details>}
    {!reports.length && <p>รอบนี้ไม่มีรายงานจากทีมที่ผ่านการตรวจและบันทึกไว้</p>}
  </>;
}
function DatePicker({ availableDates, selectedDate, onDateSelect }: { availableDates: string[]; selectedDate?: string; onDateSelect?: (value: string) => void }) {
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Bangkok" }).format(new Date());
  const oldest = availableDates[availableDates.length - 1];
  return <div className="team-report-date-picker"><label>เลือกวันที่<input type="date" min={oldest} max={today} disabled={!availableDates.length} value={selectedDate ?? ""} onChange={e => { if (e.target.value) onDateSelect?.(e.target.value); }} /></label></div>;
}
