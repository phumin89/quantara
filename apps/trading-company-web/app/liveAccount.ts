import { requestJson } from "./requestJson";

export type LiveAccount = {
  accountId: { value: string }; brokerAccountReference: string;
  snapshot: { accountId: { value: string }; version: number; observedAt: string; reconciled: boolean;
    cashBalance: number; cashReserved: number; equity: number;
    positions: { symbol: string; quantity: number; reservedQuantity: number; marketPrice: number }[] };
  mandate: { version: number; capitalAllocation: number; maximumOrderNotional: number; maximumDailyLoss: number };
  orders: { intentId: string; symbol: string; side: "Buy" | "Sell"; state: string;
    quantity: number; filledQuantity: number; reservedCash: number; reservedQuantity: number; updatedAt: string }[];
};
type AccountAuthority = { accountId?: { value: string }; snapshotVersion?: number; mandateVersion?: number };
const object = (v: unknown): v is Record<string, any> => !!v && typeof v === "object" && !Array.isArray(v);
const positive = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v) && v > 0;
const amount = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v) && v >= 0;
const version = (v: unknown) => positive(v) && Number.isSafeInteger(v);
const uuid = (v: unknown) => typeof v === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v);
const supported = (v: unknown) => ["BTC-THB", "ETH-THB", "SOL-THB"].includes(String(v));
const timestamp = (v: unknown) => typeof v === "string" && /^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(v) &&
  Number.isFinite(Date.parse(v)) && new Date(`${v.slice(0, 10)}T00:00:00Z`).toISOString().slice(0, 10) === v.slice(0, 10);
export const orderLabels: Record<string, string> = {
  Proposed: "ข้อเสนอ", Rejected: "ไม่ผ่านการอนุมัติ", Reserved: "กันวงเงินแล้ว", Dispatching: "กำลังส่ง",
  Unknown: "ยังยืนยันผลไม่ได้", Acknowledged: "ตลาดรับคำสั่งแล้ว", Open: "รอจับคู่",
  PartiallyFilled: "จับคู่บางส่วน", Filled: "จับคู่ครบแล้ว", Cancelled: "ยกเลิกแล้ว",
};
export function validLiveAccount(v: unknown, authority: AccountAuthority, now = Date.now()): v is LiveAccount {
  if (!object(v) || !object(v.accountId) || !uuid(v.accountId.value) ||
    v.accountId.value !== authority.accountId?.value || typeof v.brokerAccountReference !== "string" || !v.brokerAccountReference.trim()) return false;
  const s = v.snapshot, m = v.mandate;
  if (!object(s) || !object(s.accountId) || s.accountId.value !== v.accountId.value ||
    !version(s.version) || s.version !== authority.snapshotVersion || s.reconciled !== true || !timestamp(s.observedAt) ||
    Date.parse(s.observedAt) > now || now - Date.parse(s.observedAt) >= 30000 ||
    ![s.cashBalance, s.cashReserved, s.equity].every(amount) || s.cashReserved > s.cashBalance ||
    !object(m) || !version(m.version) || m.version !== authority.mandateVersion ||
    ![m.capitalAllocation, m.maximumOrderNotional, m.maximumDailyLoss].every(positive) ||
    !Array.isArray(s.positions) || !s.positions.every((p: unknown) => object(p) && supported(p.symbol) &&
      positive(p.quantity) && amount(p.reservedQuantity) && p.reservedQuantity <= p.quantity && positive(p.marketPrice)) ||
    new Set(s.positions.map((p: any) => p.symbol)).size !== s.positions.length) return false;
  const computed = s.cashBalance + s.positions.reduce((sum: number, p: any) => sum + p.quantity * p.marketPrice, 0);
  if (!Number.isFinite(computed) || Math.abs(computed - s.equity) > 1e-8) return false;
  return Array.isArray(v.orders) && v.orders.every((o: unknown) => object(o) && uuid(o.intentId) && supported(o.symbol) &&
    ["Buy", "Sell"].includes(o.side) && Object.hasOwn(orderLabels, o.state) && positive(o.quantity) &&
    [o.filledQuantity, o.reservedCash, o.reservedQuantity].every(amount) && timestamp(o.updatedAt) &&
    Date.parse(o.updatedAt) <= now) && new Set(v.orders.map((o: any) => o.intentId)).size === v.orders.length;
}
export async function readLiveAccount(origin: string, authority: AccountAuthority, fetcher: typeof fetch = fetch,
  signal?: AbortSignal): Promise<LiveAccount | undefined> {
  if (origin !== "https://localhost:3000" || !uuid(authority.accountId?.value) ||
    !version(authority.snapshotVersion) || !version(authority.mandateVersion)) return;
  try {
    const result = await requestJson("https://localhost:7443/live/account", { credentials: "include", signal }, fetcher);
    if (result.status === 200 && validLiveAccount(result.data, authority)) return result.data;
  } catch { /* Never expose raw executor errors or substitute a paper balance. */ }
}
