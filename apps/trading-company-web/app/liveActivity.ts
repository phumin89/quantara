import { requestJson } from "./requestJson";

export type LiveActivity = {
  accountContext: { mode: 1; accountId: string }; meetingId: string | null; observedAt: string;
  roles: { role: string; status: "idle" | "working" | "completed" | "failed"; changedAt: string }[];
  coordinatorStatus?: string; researchSymbols?: string[]; tradingSymbols?: string[];
};
const roles = ["ceo", "chief-of-staff", "market-research", "security-analysis", "quant-research", "portfolio-initial", "risk-critic", "portfolio-final"];
export const activityLifetime = 45000;
const object = (v: unknown): v is Record<string, any> => !!v && typeof v === "object" && !Array.isArray(v);
const timestamp = (v: unknown): v is string => typeof v === "string" && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(v) &&
  Number.isFinite(Date.parse(v)) && new Date(`${v.slice(0, 19)}Z`).toISOString().slice(0, 19) === v.slice(0, 19);
const symbols = ["BTC-THB", "ETH-THB", "SOL-THB"];
const validSymbols = (v: unknown) => Array.isArray(v) && v.length <= 3 && v.every(s => typeof s === "string" && symbols.includes(s)) && new Set(v).size === v.length;
export function validLiveActivity(v: unknown, accountId: string, now = Date.now()): v is LiveActivity {
  const bound = accountId.replaceAll("-", "").toLowerCase();
  if (!/^[0-9a-f]{32}$/.test(bound) || /^0{32}$/.test(bound) || !object(v) || !object(v.accountContext) ||
    v.accountContext.mode !== 1 || v.accountContext.accountId !== bound || !timestamp(v.observedAt) ||
    Date.parse(v.observedAt) > now + 5000 || now - Date.parse(v.observedAt) >= activityLifetime ||
    !Array.isArray(v.roles) || v.roles.length > 8 ||
    (v.coordinatorStatus !== undefined && (typeof v.coordinatorStatus !== "string" || !/^[A-Z_]{1,100}$/.test(v.coordinatorStatus))) ||
    (v.researchSymbols !== undefined && !validSymbols(v.researchSymbols)) ||
    (v.tradingSymbols !== undefined && (!validSymbols(v.tradingSymbols) || v.tradingSymbols.some((s: unknown) => !Array.isArray(v.researchSymbols) || !v.researchSymbols.includes(s as string)))) ||
    !(v.meetingId === null && v.roles.length === 0 || typeof v.meetingId === "string" &&
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v.meetingId) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v.meetingId))) return false;
  return v.roles.every((r: unknown) => object(r) && roles.includes(r.role) && ["idle", "working", "completed", "failed"].includes(r.status) &&
    timestamp(r.changedAt) && Date.parse(r.changedAt) <= Date.parse(v.observedAt)) &&
    new Set(v.roles.map((r: any) => r.role)).size === v.roles.length && v.roles.filter((r: any) => r.status === "working").length <= 1;
}
export async function readLiveActivity(origin: string, accountId: string, fetcher: typeof fetch = fetch, signal?: AbortSignal): Promise<LiveActivity | undefined> {
  if (origin !== "https://localhost:3000") return;
  try {
    const result = await requestJson("https://localhost:7443/live/activity", { credentials: "include", signal }, fetcher);
    const activity = (result.data as { activity?: unknown })?.activity;
    if (result.status === 200 && validLiveActivity(activity, accountId)) return activity;
  } catch { /* Display-only failures clear actors and never change trading state. */ }
}
