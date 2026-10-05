import { requestJson } from "./requestJson";
export type BitkubBalance = { currency: string; available: number; reserved: number; total: number };
export type BitkubWallet = { source: "Bitkub"; observedAt: string; credentialReference: string; balances: BitkubBalance[]; readAccessVerified: true; tradingEnabled: false; accountIdentityVerified: false };
const object = (v: unknown): v is Record<string, any> => !!v && typeof v === "object" && !Array.isArray(v);
const amount = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v) && v >= 0;
export function validBitkubWallet(v: unknown, now = Date.now()): v is BitkubWallet {
  if (!object(v) || v.source !== "Bitkub" || typeof v.observedAt !== "string" || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(v.observedAt) || !Number.isFinite(Date.parse(v.observedAt)) || new Date(`${v.observedAt.slice(0, 10)}T00:00:00Z`).toISOString().slice(0, 10) !== v.observedAt.slice(0, 10) || Date.parse(v.observedAt) > now || now - Date.parse(v.observedAt) >= 30000 || typeof v.credentialReference !== "string" || !/^sha256:[0-9a-f]{64}$/.test(v.credentialReference) || v.readAccessVerified !== true || v.tradingEnabled !== false || v.accountIdentityVerified !== false || !Array.isArray(v.balances) || v.balances.length > 256) return false;
  const seen = new Set<string>();
  return v.balances.every((b: unknown) => object(b) && typeof b.currency === "string" && /^[A-Z0-9]{1,20}$/.test(b.currency) && !seen.has(b.currency) && seen.add(b.currency) && [b.available, b.reserved, b.total].every(amount) && Math.abs(b.total - b.available - b.reserved) <= 1e-8) && seen.has("THB");
}
export async function readBitkubWallet(origin: string, fetcher: typeof fetch = fetch, signal?: AbortSignal): Promise<BitkubWallet | undefined> {
  if (origin !== "https://localhost:3000") return;
  try { const result = await requestJson("https://localhost:7443/live/wallet", { credentials: "include", cache: "no-store", signal }, fetcher); return result.status === 200 && validBitkubWallet(result.data) ? result.data : undefined; } catch { return; }
}
