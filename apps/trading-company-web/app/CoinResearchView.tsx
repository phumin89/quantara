import React from "react";
import { validThbSpotSymbol as symbol } from "./liveActivity";

type Candidate = { symbol: string; bid: number; ask: number; quoteVolume24h: number; change24hPercent: number; spreadPercent: number; depthQuoteNotional: number; executionAllowed: boolean; held: boolean };
type Source = { id: string; symbol: string; title: string; url: string; contentHash: string; retrievedAt: string };
type Claim = { statement: string; assessment: "PROJECT_CLAIM" | "CONTRADICTED" | "UNCERTAIN"; sourceId: string; quote: string };
type Finding = { symbol: string; summary: string; claims: Claim[] };
type NewsItem = { id: string; source: string; kind: "OFFICIAL" | "REPORT"; title: string; url: string; sourceAt: string; dateKind: "PUBLISHED" | "UPDATED"; retrievedAt: string; contentHash: string; symbols: string[]; marketWide: boolean; evidenceText: string };
type NewsBrief = { observedAt: string; changeKey: string; items: NewsItem[]; coverage: { source: string; status: "CURRENT" | "UNAVAILABLE" }[] };
export type ResearchBrief = { observedAt: string; changeKey: string; watchlist: Candidate[]; sources: Source[]; findings: Finding[]; gaps: string[]; news?: NewsBrief };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === "object" && !Array.isArray(v);
const text = (v: unknown, max: number): v is string => typeof v === "string" && v.trim().length > 0 && v.length <= max && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/.test(v);
const hash = (v: unknown): v is string => typeof v === "string" && /^[0-9a-f]{64}$/i.test(v);
const number = (v: unknown, max = 1e18): v is number => typeof v === "number" && Number.isFinite(v) && v >= 0 && v <= max;
const isoTime = (v: unknown): v is string => typeof v === "string" && /^\d{4}-\d{2}-\d{2}T/.test(v) && Number.isFinite(Date.parse(v));
const url = (v: unknown): v is string => { try { const u = new URL(String(v)); return u.protocol === "https:" && u.port === "" && !u.username && !u.password && !u.search && !u.hash && (["bitcoin.org", "raw.githubusercontent.com", "ethereum.org", "solana.com", "xrpl.org", "tether.to"].includes(u.hostname) || v === "https://www.kubchain.com/docs/EN_KUB_Chain_WhitePaper_V5.2.pdf"); } catch { return false; } };
const newsKinds: Record<string, NewsItem["kind"]> = { CoinDesk: "REPORT", Ethereum: "OFFICIAL", "Federal Reserve": "OFFICIAL", Bitkub: "OFFICIAL" };
const newsText = (v: unknown, max: number): v is string => text(v, max) && !/[\u0000-\u001f\u007f-\u009f]/.test(v);
const newsHash = (v: unknown): v is string => typeof v === "string" && /^[0-9a-f]{64}$/.test(v);
function newsTime(v: unknown): v is string {
  if (typeof v !== "string" || v.startsWith("0000-")) return false;
  const match = /^(\d{4}-\d{2}-\d{2})T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-](?:(?:0\d|1[0-3]):[0-5]\d|14:00))$/.exec(v);
  if (!match || !Number.isFinite(Date.parse(v))) return false;
  const day = Date.parse(`${match[1]}T00:00:00Z`);
  return Number.isFinite(day) && new Date(day).toISOString().slice(0, 10) === match[1];
}
function newsUrl(source: string, value: unknown): value is string {
  if (typeof value !== "string" || value.length > 400 || /[\u0000-\u0020\u007f-\u009f]/.test(value)) return false;
  try {
    const u = new URL(value);
    if (u.protocol !== "https:" || u.port || u.username || u.password || value.includes("#") || (source !== "Bitkub" && value.includes("?"))) return false;
    switch (source) {
      case "CoinDesk": return u.hostname === "www.coindesk.com" && !u.search && u.pathname.length > 1;
      case "Ethereum": return u.hostname === "blog.ethereum.org" && !u.search && u.pathname.startsWith("/en/");
      case "Federal Reserve": return u.hostname === "www.federalreserve.gov" && !u.search && u.pathname.startsWith("/newsevents/pressreleases/");
      case "Bitkub": return u.hostname === "support.bitkub.com" && u.pathname === "/en/solutions/articles" && /^\?id=KM[0-9]{9}$/.test(u.search);
      default: return false;
    }
  } catch { return false; }
}
function readNews(value: unknown, observed: number): NewsBrief | undefined {
  if (!object(value) || !newsTime(value.observedAt) || Date.parse(value.observedAt) > observed || observed - Date.parse(value.observedAt) > 16 * 60 * 1000 || !newsHash(value.changeKey) || !Array.isArray(value.items) || value.items.length > 12 || !Array.isArray(value.coverage) || value.coverage.length < 1 || value.coverage.length > 4) return;
  const coverage: NewsBrief["coverage"] = []; const covered = new Set<string>();
  for (const c of value.coverage) {
    if (!object(c) || typeof c.source !== "string" || !Object.hasOwn(newsKinds, c.source) || (c.status !== "CURRENT" && c.status !== "UNAVAILABLE") || covered.has(c.source)) return;
    covered.add(c.source); coverage.push(c as NewsBrief["coverage"][number]);
  }
  const newsObserved = Date.parse(value.observedAt); const ids = new Set<string>(); const urls = new Set<string>(); const items: NewsItem[] = [];
  for (const i of value.items) {
    if (!object(i) || typeof i.source !== "string" || !Object.hasOwn(newsKinds, i.source) || newsKinds[i.source] !== i.kind || !newsText(i.title, 180) || !newsText(i.evidenceText, 280) || !newsUrl(i.source, i.url) || (i.dateKind !== "PUBLISHED" && i.dateKind !== "UPDATED") || !newsHash(i.contentHash) || i.id !== `news-${i.contentHash}` || ids.has(i.id) || urls.has(i.url) || !newsTime(i.sourceAt) || Date.parse(i.sourceAt) > newsObserved || newsObserved - Date.parse(i.sourceAt) > 7 * 24 * 60 * 60 * 1000 || !newsTime(i.retrievedAt) || Date.parse(i.retrievedAt) > newsObserved || newsObserved - Date.parse(i.retrievedAt) > 16 * 60 * 1000 || !Array.isArray(i.symbols) || i.symbols.length > 8 || i.symbols.some(s => !symbol(s)) || new Set(i.symbols).size !== i.symbols.length || typeof i.marketWide !== "boolean" || !coverage.some(c => c.source === i.source && c.status === "CURRENT")) return;
    ids.add(i.id); urls.add(i.url); items.push(i as NewsItem);
  }
  if (new TextEncoder().encode(JSON.stringify(value)).length > 12 * 1024) return;
  return { observedAt: value.observedAt, changeKey: value.changeKey, items, coverage };
}
export function readResearch(value: unknown, now = Date.now()): ResearchBrief | undefined {
  if (!object(value) || !isoTime(value.observedAt) || Date.parse(value.observedAt) > now || !hash(value.changeKey) || !Array.isArray(value.watchlist) || value.watchlist.length > 8 || !Array.isArray(value.sources) || value.sources.length > 16 || !Array.isArray(value.findings) || value.findings.length > 8 || !Array.isArray(value.gaps) || value.gaps.length > 12) return;
  const watchlist: Candidate[] = []; const symbols = new Set<string>();
  for (const c of value.watchlist) { if (!object(c) || !symbol(c.symbol) || symbols.has(c.symbol) || !number(c.bid, 1e15) || c.bid <= 0 || !number(c.ask, 1e15) || c.ask < c.bid || !number(c.quoteVolume24h) || typeof c.change24hPercent !== "number" || !Number.isFinite(c.change24hPercent) || c.change24hPercent < -100 || c.change24hPercent > 1000000 || !number(c.spreadPercent, 100000) || !number(c.depthQuoteNotional) || typeof c.executionAllowed !== "boolean" || typeof c.held !== "boolean") return; symbols.add(c.symbol); watchlist.push(c as Candidate); }
  const sources: Source[] = []; const sourceIds = new Set<string>();
  const observed = Date.parse(value.observedAt as string);
  const news = value.news == null ? undefined : readNews(value.news, observed);
  if (value.news != null && !news) return;
  for (const s of value.sources) { if (!object(s) || !symbol(s.symbol) || !text(s.title, 150) || !url(s.url) || !hash(s.contentHash) || s.id !== `doc-${s.contentHash}` || sourceIds.has(s.id) || !isoTime(s.retrievedAt) || Date.parse(s.retrievedAt) > observed || observed - Date.parse(s.retrievedAt) > 2 * 24 * 60 * 60 * 1000) return; sourceIds.add(s.id); sources.push(s as Source); }
  const findings: Finding[] = []; const findingSymbols = new Set<string>();
  for (const f of value.findings) { if (!object(f) || !symbol(f.symbol) || findingSymbols.has(f.symbol) || !text(f.summary, 600) || !Array.isArray(f.claims) || f.claims.length < 1 || f.claims.length > 3) return; const claims: Claim[] = []; for (const c of f.claims) { const sourceId = object(c) ? String(c.sourceId) : ""; if (!object(c) || !text(c.statement, 400) || !text(c.quote, 400) || c.quote.length < 20 || !["PROJECT_CLAIM", "CONTRADICTED", "UNCERTAIN"].includes(String(c.assessment)) || !sourceIds.has(sourceId) || !sources.some(s => s.id === sourceId && s.symbol === f.symbol)) return; claims.push(c as Claim); } findingSymbols.add(f.symbol); findings.push({ symbol: f.symbol, summary: f.summary, claims }); }
  if (value.gaps.some(g => !text(g, 200)) || JSON.stringify(value).length > 24 * 1024) return;
  return { observedAt: value.observedAt as string, changeKey: value.changeKey as string, watchlist, sources, findings, gaps: value.gaps as string[], ...(news ? { news } : {}) };
}
const assessment: Record<string, string> = { PROJECT_CLAIM: "ข้อมูลจากโครงการ", CONTRADICTED: "หลักฐานขัดแย้ง", UNCERTAIN: "ยังยืนยันไม่ได้" };
const money = new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB", maximumFractionDigits: 2 });
const percent = new Intl.NumberFormat("th-TH", { maximumFractionDigits: 2 });
export function CoinResearchView({ research, businessDate }: { research: unknown; businessDate?: string }) {
  const data = readResearch(research);
  if (!data || (businessDate && new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Bangkok" }).format(new Date(data.observedAt)) !== businessDate)) return null;
  return <details className="team-research">
    <summary>เหรียญและหลักฐานที่ทีมตรวจ</summary>
    <p>สำรวจตลาด ณ <time dateTime={data.observedAt}>{new Date(data.observedAt).toLocaleString("th-TH", { timeZone: "Asia/Bangkok" })}</time></p>
    {data.news && <details>
      <summary>ข่าวล่าสุดของรอบนี้ · {data.news.items.length} รายการ</summary>
      <p>{data.news.coverage.map(c => `${c.source}: ${c.status === "CURRENT" ? "อ่านได้" : "ไม่พร้อม"}`).join(" · ")}</p>
      {data.news.items.length > 0 && <ul>{data.news.items.map(i => <li key={i.id}>
        <a href={i.url} target="_blank" rel="noreferrer">{i.title}</a>
        <div>{i.source} · {i.kind === "OFFICIAL" ? "ประกาศทางการ" : "รายงานข่าว"} · {i.dateKind === "UPDATED" ? "อัปเดต" : "เผยแพร่"} <time dateTime={i.sourceAt}>{new Date(i.sourceAt).toLocaleString("th-TH", { timeZone: "Asia/Bangkok" })}</time></div>
        {(i.marketWide || i.symbols.length > 0) && <div>{[...(i.marketWide ? ["ตลาดรวม"] : []), ...i.symbols].join(" · ")}</div>}
        <details><summary>ข้อความอ้างอิงข่าว</summary><p>{i.evidenceText}</p></details>
      </li>)}</ul>}
    </details>}
    <ul>{data.watchlist.map(c => <li key={c.symbol}><strong>{c.symbol}</strong> · {c.held ? "ถืออยู่" : "ยังไม่ได้ถือ"} · {c.executionAllowed ? "ในขอบเขตเทรด" : "วิจัยเท่านั้น"} · สเปรด {percent.format(c.spreadPercent)}% · ปริมาณ 24 ชม. {money.format(c.quoteVolume24h)}</li>)}</ul>
    {data.findings.map(f => <article className="team-role-report" key={f.symbol}><h4>{f.symbol}</h4><p>{f.summary}</p><ul>{f.claims.map((c, i) => { const source = data.sources.find(s => s.id === c.sourceId); return <li key={`${c.sourceId}-${i}`}>{assessment[c.assessment]}: {c.statement} <a href={source?.url} target="_blank" rel="noreferrer">{source?.title}</a><details><summary>ดูข้อความอ้างอิง</summary><p style={{ overflowWrap: "anywhere" }}>{c.quote}</p></details></li>; })}</ul></article>)}
    {data.gaps.length > 0 && <p>ข้อมูลที่ยังขาด: {data.gaps.join(" · ")}</p>}
  </details>;
}
