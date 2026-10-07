import React from "react";

const roles = ["ceo", "chief-of-staff", "market-research", "security-analysis", "quant-research", "portfolio-initial", "risk-critic", "portfolio-final"];
type Scope = { mode: 1; accountId: string };
type Lesson = { artifactId: string; quote: string; lesson: string; nextPractice: string };
type Role = { role: string; status: "ACCEPTED" | "NO_HISTORY" | "INVALID"; reportsReviewed: number; lesson: Lesson | null };
export type Learning = { businessDate: string; accountContext: Scope; roles: Role[]; modelCalls: number };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === "object" && !Array.isArray(v);
const text = (v: unknown, limit: number): v is string => typeof v === "string" && v.trim().length > 0 && v.length <= limit && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/.test(v);
const date = (v: unknown): v is string => typeof v === "string" && /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v)) && new Date(v).toISOString().startsWith(v);
export function readLearning(v: unknown, scope: Scope, meetingDate: string): Learning | undefined {
  if (!object(v) || !date(v.businessDate) || !date(meetingDate) || v.businessDate > meetingDate || Date.parse(meetingDate) - Date.parse(v.businessDate) > 7 * 86400000 ||
      !object(v.accountContext) || v.accountContext.mode !== 1 || scope.mode !== 1 || v.accountContext.accountId !== scope.accountId ||
      !/^[0-9a-f]{32}$/i.test(scope.accountId) || /^0{32}$/.test(scope.accountId) || ![0, 1].includes(Number(v.modelCalls)) || typeof v.modelCalls !== "number" ||
      !Array.isArray(v.roles) || v.roles.length !== 8) return;
  for (let i = 0; i < v.roles.length; i++) {
    const r = v.roles[i];
    if (!object(r) || r.role !== roles[i]) return;
    if (r.status === "ACCEPTED") {
      const l = r.lesson;
      if (r.reportsReviewed !== 1 || !object(l) || typeof l.artifactId !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(l.artifactId) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(l.artifactId) ||
          !text(l.quote, 200) || l.quote.length < 20 || !text(l.lesson, 250) || !text(l.nextPractice, 250)) return;
    } else if (!["NO_HISTORY", "INVALID"].includes(String(r.status)) || r.reportsReviewed !== 0 || r.lesson !== null) return;
  }
  if (new TextEncoder().encode(JSON.stringify(v)).length > 16 * 1024) return;
  return v as Learning;
}
export function CompanyLearningView({ learning, scope, meetingDate, labels }: { learning: unknown; scope: Scope; meetingDate: string; labels: Record<string, string> }) {
  const data = readLearning(learning, scope, meetingDate);
  if (!data) return null;
  const accepted = data.roles.filter(r => r.status === "ACCEPTED").length;
  return <details className="team-research"><summary>บทเรียนที่ใช้ในรอบนี้ · {accepted}/8 บทบาท</summary>
    <p>ทบทวน <time dateTime={data.businessDate}>{new Date(data.businessDate).toLocaleDateString("th-TH", { timeZone: "Asia/Bangkok" })}</time> · บทเรียนจากรายงานเดิม ยังไม่ใช่ผลวัดประสิทธิภาพ</p>
    {data.roles.map(r => <article className="team-role-report" key={r.role}><h4>{labels[r.role]}</h4>{r.lesson ? <><p>{r.lesson.lesson}</p><p><strong>รอบถัดไป:</strong> {r.lesson.nextPractice}</p>
      <details><summary>ดูหลักฐานบทเรียน</summary><blockquote style={{ overflowWrap: "anywhere" }}>{r.lesson.quote}</blockquote></details></> : <p>{r.status === "NO_HISTORY" ? "ยังไม่มีรายงานของบทบาทนี้ให้ทบทวน" : "บทเรียนยังไม่ผ่านการตรวจหลักฐาน"}</p>}</article>)}
  </details>;
}
