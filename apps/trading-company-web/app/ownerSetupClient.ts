import { requestJson } from "./requestJson";
export const ownerOrigin = "https://localhost:3000";
export const executorOrigin = "https://localhost:7443";
export type OwnerMandate = { version: number; capitalAllocation: number; maximumOrderNotional: number; maximumDailyTurnover: number; maximumDailyLoss: number; maximumDrawdown: number; maximumPortfolioExposure: number; maximumOpenPositions: number; maximumSlippage: number; feeReserveRate: number; allowedSymbols: string[]; expiresAt: string | null; automaticDailyRollover: boolean };
export type OwnerSetup = { configured: boolean; canControl: boolean; reasons: string[]; accountId?: { value: string }; brokerAccountReference?: string; verifiedSymbols?: string[]; availableCapital?: number; fullSpotCapitalAllowed?: boolean; minimumFeeReserveRate?: number; provisioned?: boolean; mandate?: OwnerMandate | null; snapshotVersion?: number | null; mandateVersion?: number | null; baselineEstablished?: boolean; needsDailyBaseline?: boolean; armed?: boolean; stopped?: boolean; reconciled?: boolean; observedAt?: string | null };
const amount = (x: unknown): x is number => typeof x === "number" && Number.isFinite(x) && x >= 0 && x <= 1e10;
const version = (x: unknown): x is number => typeof x === "number" && Number.isSafeInteger(x) && x > 0;
const object = (x: unknown): x is Record<string, unknown> => !!x && typeof x === "object" && !Array.isArray(x);
const uuid = (x: unknown) => typeof x === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(x);
const symbols = (x: unknown): x is string[] => Array.isArray(x) && x.length > 0 && x.length <= 3 && new Set(x).size === x.length && x.every(s => ["BTC-THB", "ETH-THB", "SOL-THB"].includes(s));
export function readOwnerSetup(x: unknown): OwnerSetup | undefined {
  if (!object(x) || typeof x.configured !== "boolean" || typeof x.canControl !== "boolean" || !Array.isArray(x.reasons) || x.reasons.length > 64 || !x.reasons.every(r => typeof r === "string" && r.length <= 256)) return;
  if (!x.configured) return { configured: false, canControl: x.canControl, reasons: x.reasons };
  if (!object(x.accountId) || !uuid(x.accountId.value) || typeof x.brokerAccountReference !== "string" || !x.brokerAccountReference.trim() || x.brokerAccountReference.length > 256 ||
      !symbols(x.verifiedSymbols) || !amount(x.availableCapital) || x.fullSpotCapitalAllowed !== true || !amount(x.minimumFeeReserveRate) || x.minimumFeeReserveRate <= 0 || x.minimumFeeReserveRate > .1 ||
      ["provisioned", "baselineEstablished", "needsDailyBaseline", "armed", "stopped", "reconciled"].some(k => typeof x[k] !== "boolean")) return;
  if (x.provisioned) {
    const m = x.mandate;
    if (!version(x.snapshotVersion) || !version(x.mandateVersion) || !object(m) || m.version !== x.mandateVersion || !symbols(m.allowedSymbols) ||
        ["capitalAllocation", "maximumOrderNotional", "maximumDailyTurnover", "maximumDailyLoss", "maximumDrawdown", "maximumPortfolioExposure", "feeReserveRate"].some(k => !amount(m[k]) || (m[k] as number) <= 0) ||
        (m.maximumDrawdown as number) > 1 || (m.maximumPortfolioExposure as number) > 1 || (m.feeReserveRate as number) > .1 || !version(m.maximumOpenPositions) ||
        !amount(m.maximumSlippage) || m.maximumSlippage > .02 || typeof m.automaticDailyRollover !== "boolean" ||
        !(m.expiresAt === null || typeof m.expiresAt === "string" && Number.isFinite(Date.parse(m.expiresAt))) ||
        typeof x.observedAt !== "string" || !Number.isFinite(Date.parse(x.observedAt))) return;
  } else if (x.mandate != null || x.armed || x.baselineEstablished || x.reconciled) return;
  return x as OwnerSetup;
}
export function fullSpotMandate(setup: OwnerSetup) {
  if (!setup.configured || !setup.canControl || setup.fullSpotCapitalAllowed !== true || !setup.accountId || !amount(setup.availableCapital) || setup.availableCapital <= 0 ||
      !symbols(setup.verifiedSymbols) || !setup.minimumFeeReserveRate) return;
  const capital = setup.availableCapital;
  return { accountId: setup.accountId, expectedVersion: setup.provisioned ? setup.mandateVersion : 0, capitalAllocation: capital,
    maximumOrderNotional: capital, maximumDailyTurnover: capital, maximumDailyLoss: capital, maximumDrawdown: 1, maximumPortfolioExposure: 1,
    maximumOpenPositions: setup.verifiedSymbols.length, maximumSlippage: .02, feeReserveRate: setup.minimumFeeReserveRate,
    allowedSymbols: setup.verifiedSymbols, expiresAt: null, automaticDailyRollover: true };
}
export function sameOwnerConsent(before: OwnerSetup, after: OwnerSetup) {
  return before.configured === after.configured && before.canControl === after.canControl &&
    before.accountId?.value === after.accountId?.value && before.brokerAccountReference === after.brokerAccountReference &&
    before.provisioned === after.provisioned && before.mandateVersion === after.mandateVersion && before.stopped === after.stopped &&
    before.availableCapital === after.availableCapital && before.minimumFeeReserveRate === after.minimumFeeReserveRate &&
    JSON.stringify(before.verifiedSymbols) === JSON.stringify(after.verifiedSymbols);
}
const actions = new Set(["/live/provision", "/live/mandate", "/live/baseline", "/live/day-baseline", "/live/reconcile", "/live/arm"]);
export async function ownerMutation(origin: string, path: string, body: unknown, fetcher: typeof fetch = fetch, signal?: AbortSignal) {
  if (origin !== ownerOrigin || !actions.has(path)) return { status: 0, data: undefined };
  const csrf = await requestJson(`${executorOrigin}/owner/csrf`, { credentials: "include", signal }, fetcher);
  if (csrf.status !== 200 || !object(csrf.data) || typeof csrf.data.token !== "string" || !csrf.data.token) return { status: csrf.status, data: undefined };
  return requestJson(`${executorOrigin}${path}`, { method: "POST", credentials: "include", signal, headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf.data.token }, body: JSON.stringify(body) }, fetcher);
}
