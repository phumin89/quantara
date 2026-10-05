export type Position = { symbol: { value: string }; quantity: number; averagePrice: number; marketPrice: number; marketValue: number; unrealizedPnl: number };
export type Receipt = { orderId: string; quote: { symbol: string; nativePrice: number; currency: string; thbPerUnit: number; observedAt?: string; availableAt?: string; source: string }; status: string; violations: string[] };
export type Portfolio = { fundingAccountId?: string; cash: number; totalContributed: number; totalPnl: number; equity: number; realizedPnl?: number; drawdown?: number; initialCapital?: number; positions?: Position[]; pendingOrders?: { id: string; symbol: string; side: 0 | 1; quantity: number; decisionAt: string }[]; receipts?: Receipt[]; currency: "THB"; tradingStatus: string; credits?: { salaryDate: string; amount: number; creditedAt: string }[] };
export type FundingStatus = { status: string; pendingInvestment?: number | null; portfolio?: Portfolio | null; account?: { accountId: string; nextPayday: string; totalSalary: number; emergencyBalance: number; investmentCash: number; mandate: { monthlySalary: number; fixedEssentials: number; fixedDebtPayment: number }; postings: { postedAt: string; allocation: { salaryDate: string; salary: number; essentials: number; debt: number; emergency: number; investment: number; discretionary: number } }[] } | null };
export type SavedReport = { role: string; summary: string; providerModel: string; artifactId: string; dataCutoff: string };
export type SavedMeeting = { businessDate: string; status: string; summary?: { modelCalls: number; inputTokens: number; outputTokens: number; reports: SavedReport[] } | null };
export type CompanyStatus = { status: string; modelEnabled: boolean; model?: string | null; error?: string | null; schedule?: string | null; supportedSymbols?: string[]; executionCosts?: string; lastMeetingDate?: string | null; lastMeeting?: SavedMeeting | null; quoteCredentialsConfigured?: boolean | null; publicCryptoQuotes?: boolean; equityQuotesConfigured?: boolean | null; paused?: boolean; busy?: boolean };

type RecordValue = Record<string, unknown>;
const object = (v: unknown): v is RecordValue => Boolean(v) && typeof v === "object" && !Array.isArray(v);
const text = (v: unknown): v is string => typeof v === "string" && v.length > 0;
const number = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);
const date = (v: unknown) => text(v) && /^\d{4}-\d{2}-\d{2}(?:T.*)?$/.test(v) && Number.isFinite(Date.parse(v)) &&
  new Date(`${v.slice(0, 10)}T00:00:00Z`).toISOString().slice(0, 10) === v.slice(0, 10);
const numbers = (v: RecordValue, keys: string[]) => keys.every(k => number(v[k]));
const optionalNumber = (v: RecordValue, keys: string[]) => keys.every(k => v[k] == null || number(v[k]));
const array = (v: unknown, check: (item: unknown) => boolean) => v === undefined || Array.isArray(v) && v.every(check);

export function validFundingStatus(v: unknown): v is FundingStatus {
  if (!object(v) || !text(v.status) || v.pendingInvestment != null && !number(v.pendingInvestment)) return false;
  const a = v.account;
  if (a != null && (!object(a) || !text(a.accountId) || !date(a.nextPayday) || !numbers(a, ["totalSalary", "emergencyBalance", "investmentCash"]) ||
    !object(a.mandate) || !numbers(a.mandate, ["monthlySalary", "fixedEssentials", "fixedDebtPayment"]) || !Array.isArray(a.postings) ||
    !a.postings.every(p => object(p) && date(p.postedAt) && object(p.allocation) && date(p.allocation.salaryDate) && numbers(p.allocation, ["salary", "essentials", "debt", "emergency", "investment", "discretionary"])))) return false;
  const p = v.portfolio;
  if (p == null) return true;
  return object(p) && p.currency === "THB" && text(p.tradingStatus) && numbers(p, ["cash", "totalContributed", "totalPnl", "equity"]) && optionalNumber(p, ["realizedPnl", "drawdown", "initialCapital"]) &&
    array(p.positions, x => object(x) && object(x.symbol) && text(x.symbol.value) && numbers(x, ["quantity", "averagePrice", "marketPrice", "marketValue", "unrealizedPnl"])) &&
    array(p.pendingOrders, x => object(x) && text(x.id) && text(x.symbol) && (x.side === 0 || x.side === 1) && number(x.quantity) && x.quantity > 0 && date(x.decisionAt)) &&
    array(p.receipts, x => object(x) && text(x.orderId) && text(x.status) && Array.isArray(x.violations) && x.violations.every(text) && object(x.quote) && text(x.quote.symbol) && ["USD", "THB"].includes(String(x.quote.currency)) && text(x.quote.source) && numbers(x.quote, ["nativePrice", "thbPerUnit"])) &&
    array(p.credits, x => object(x) && date(x.salaryDate) && date(x.creditedAt) && number(x.amount));
}

export function validCompanyStatus(v: unknown): v is CompanyStatus {
  if (!object(v) || !text(v.status) || typeof v.modelEnabled !== "boolean") return false;
  for (const k of ["model", "error", "schedule", "executionCosts"]) if (v[k] != null && typeof v[k] !== "string") return false;
  for (const k of ["quoteCredentialsConfigured", "equityQuotesConfigured", "publicCryptoQuotes", "paused", "busy"]) if (v[k] != null && typeof v[k] !== "boolean") return false;
  if (v.lastMeetingDate != null && !date(v.lastMeetingDate) || !array(v.supportedSymbols, text)) return false;
  const m = v.lastMeeting;
  if (m == null) return true;
  if (!object(m) || !date(m.businessDate) || !text(m.status)) return false;
  const s = m.summary;
  return s == null || object(s) && ["modelCalls", "inputTokens", "outputTokens"].every(k => number(s[k]) && Number.isSafeInteger(s[k]) && s[k] >= 0) && Array.isArray(s.reports) &&
    s.reports.every(r => object(r) && ["role", "summary", "providerModel", "artifactId"].every(k => text(r[k])) && date(r.dataCutoff));
}
