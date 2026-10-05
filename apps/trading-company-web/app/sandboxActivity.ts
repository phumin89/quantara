import type { OfficeAgent, WorkflowEvent } from "./types";
import type { SavedMeeting } from "./forwardStatus";

export const staffIds: Record<string, string> = { "market-research": "news", "security-analysis": "technical", "quant-research": "quant", "portfolio-initial": "portfolio-manager", "portfolio-final": "portfolio-manager", "risk-critic": "risk" };
export const configured: OfficeAgent[] = [
  ["treasury", "ดูแลเงินสำรอง"], ["ceo", "ผู้บริหาร"], ["chief-of-staff", "ผู้ประสานงาน"], ["portfolio-manager", "ผู้จัดการพอร์ต"],
  ["news", "วิจัยตลาด"], ["technical", "วิเคราะห์สินทรัพย์"], ["quant", "วิเคราะห์เชิงปริมาณ"], ["risk", "ตรวจความเสี่ยง"],
].map(([id, role]) => ({ id, role, status: "configured", activity: "ทีมที่ตั้งค่าไว้", summary: "ยังไม่มีกิจกรรมใหม่ที่ได้รับในหน้าต่างนี้", artifacts: [] }));
export type Activity = { agents: OfficeAgent[]; events: WorkflowEvent[] };
export const initialActivity: Activity = { agents: configured, events: [] };

export function receiveActivity(current: Activity, event: WorkflowEvent): Activity {
  if (!event || typeof event.type !== "string" || typeof event.correlationId !== "string" || !Number.isFinite(Date.parse(event.occurredAt)) || event.accountContext && event.accountContext.mode !== 0) return current;
  const payload = event.payload;
  if (!payload || typeof payload.agentId !== "string" || typeof payload.summary !== "string") return current;
  const id = staffIds[payload.agentId] ?? payload.agentId;
  if (!configured.some(agent => agent.id === id)) return current;
  if (!(["configured", "idle", "working", "meeting", "completed", "failed"] as unknown[]).includes(payload.status)) return current;
  const events = [...current.events, event].slice(-100);
  const agents = current.agents.map(agent => agent.id !== id ? agent : event.type === "AgentDespawned" ? { ...configured.find(a => a.id === id)! } : {
    ...agent, status: payload.status, activity: typeof payload.activity === "string" ? payload.activity : agent.activity, summary: payload.summary,
    artifacts: payload.artifact && typeof payload.artifact.title === "string" && typeof payload.artifact.summary === "string" ? [...agent.artifacts, payload.artifact].slice(-20) : agent.artifacts,
  });
  return { agents, events };
}

export function staffWithSavedReports(agents: OfficeAgent[], meeting?: SavedMeeting | null): OfficeAgent[] {
  if (meeting?.status !== "COMPLETED") return agents;
  const latest = new Map(meeting.summary?.reports.map(report => [staffIds[report.role] ?? report.role, report]));
  return agents.map(agent => {
    const report = latest.get(agent.id);
    return agent.status === "configured" && report ? { ...agent, status: "completed", activity: `รายงานที่บันทึกไว้ · ${meeting.businessDate}`, summary: report.summary } : agent;
  });
}
