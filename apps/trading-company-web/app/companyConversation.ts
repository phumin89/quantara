import { requestJson } from "./requestJson";

export type ConversationProposal = { action: string; symbol: string; targetWeight: number; confidence: number; thesis: string; invalidation: string };
export type ConversationMessage = { id: string; role: string; createdAt: string; summary: string; recommendations: string[]; risks: string[]; proposals: ConversationProposal[]; evidenceStatus: "saved-report" | "accepted" };
export type ConversationDay = { businessDate: string; status: string | null; messages: ConversationMessage[]; updatedAt: string; executionVerified?: boolean };
export type ConversationDates = { dates: string[]; updatedAt: string };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === "object" && !Array.isArray(v);
const text = (v: unknown): v is string => typeof v === "string" && v.length > 0 && v.length <= 20000;
const timestamp = (v: unknown): v is string => text(v) && /^\d{4}-\d{2}-\d{2}T/.test(v) && Number.isFinite(Date.parse(v));
export const validConversationDate = (v: unknown): v is string => text(v) && /^\d{4}-\d{2}-\d{2}$/.test(v) &&
  Number.isFinite(Date.parse(`${v}T00:00:00Z`)) && new Date(`${v}T00:00:00Z`).toISOString().slice(0, 10) === v;
const texts = (v: unknown): v is string[] => Array.isArray(v) && v.length <= 32 && v.every(text);
const proposal = (v: unknown): v is ConversationProposal => object(v) && ["BUY", "SELL", "HOLD", "TRIM"].includes(String(v.action)) && text(v.symbol) &&
  typeof v.targetWeight === "number" && Number.isFinite(v.targetWeight) && v.targetWeight >= 0 && v.targetWeight <= 1 &&
  typeof v.confidence === "number" && Number.isFinite(v.confidence) && v.confidence >= 0 && v.confidence <= 1 && text(v.thesis) && text(v.invalidation);
export function validConversationDates(v: unknown): v is ConversationDates {
  return object(v) && timestamp(v.updatedAt) && Array.isArray(v.dates) && v.dates.length <= 366 && v.dates.every(validConversationDate) && new Set(v.dates).size === v.dates.length;
}
export function validConversationDay(v: unknown): v is ConversationDay {
  if (!object(v) || !validConversationDate(v.businessDate) || !(v.status === null || text(v.status)) || !timestamp(v.updatedAt) || !Array.isArray(v.messages) || v.messages.length > 512 || v.executionVerified != null && typeof v.executionVerified !== "boolean") return false;
  const ids = new Set<string>();
  return v.messages.every(m => {
    if (!object(m) || !text(m.id) || ids.has(m.id) || !text(m.role) || !timestamp(m.createdAt) || !text(m.summary) || !texts(m.recommendations) || !texts(m.risks) ||
      !Array.isArray(m.proposals) || m.proposals.length > 32 || !m.proposals.every(proposal) || !["saved-report", "accepted"].includes(String(m.evidenceStatus))) return false;
    ids.add(m.id); return true;
  });
}
export async function readConversation<T>(url: string, validate: (v: unknown) => v is T, fetcher: typeof fetch = fetch, signal?: AbortSignal): Promise<{ ok: true; data: T } | { ok: false }> {
  try {
    const r = await requestJson(url, { signal }, fetcher);
    return r.status >= 200 && r.status < 300 && validate(r.data) ? { ok: true, data: r.data } : { ok: false };
  } catch { return { ok: false }; }
}
