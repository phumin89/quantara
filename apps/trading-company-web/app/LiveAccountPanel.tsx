"use client";
import { createContext, useContext, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import Link from "next/link";
import { CompanyChat } from "./CompanyChat";
import { LiveOffice } from "./LiveOffice";
import { LiveOwnerSetup } from "./LiveOwnerSetup";
import { LiveTeamReports } from "./LiveTeamReports";
import { requestJson } from "./requestJson";
import { readLiveAccount, orderLabels } from "./liveAccount";
import type { LiveAccount } from "./liveAccount";
import { readBitkubWallet } from "./bitkubWallet";
import type { BitkubWallet } from "./bitkubWallet";

const ownerOrigin = "https://localhost:3000";
const executor = "https://localhost:7443";
type Status = { state: "DISABLED" | "BLOCKED" | "STOPPED" | "DISARMED" | "ARMED"; armed: boolean; reasons: string[];
  accountId?: { value: string }; snapshotVersion?: number; mandateVersion?: number };
type Connection = { kind: "loading" | "https-required" | "pair-required" | "unavailable" } | { kind: "connected"; status: Status };
const labels: Record<Status["state"], string> = {
  DISABLED: "ระบบเงินจริงยังไม่เปิด", BLOCKED: "หยุดรอแก้เงื่อนไขความปลอดภัย",
  STOPPED: "เจ้าของสั่งหยุดแล้ว", DISARMED: "ยังไม่อนุญาตให้ส่งคำสั่ง", ARMED: "ระบบรายงานว่าเปิดสิทธิ์ส่งคำสั่ง",
};
const reasons: Record<string, string> = {
  EXECUTOR_NOT_CONFIGURED: "บัญชีสำหรับเทรดและระบบส่งคำสั่งยังไม่พร้อม",
  ACCOUNT_NOT_PROVISIONED: "บันทึกวงเงินสำหรับ CEO และตรวจยอดเริ่มต้นที่ขั้นตั้งบัญชีด้านล่าง",
  ISOLATION_REQUIRED: "ยังไม่ผ่านการแยกสิทธิ์ระบบเงินจริงออกจาก AI",
  INVALID_MANDATE: "ต้องกำหนดวงเงินและข้อจำกัดให้ครบ",
  MANDATE_EXPIRED: "คำอนุมัติวงเงินหมดอายุ", ACCOUNT_BINDING_CHANGED: "การผูกบัญชีเปลี่ยน ต้องตรวจใหม่",
  STALE_APPROVAL: "วงเงินเปลี่ยน ต้องอนุมัติใหม่", RECONCILIATION_REQUIRED: "ต้องตรวจยอดเงินและคำสั่งกับตลาด",
  STALE_SNAPSHOT: "ข้อมูลบัญชีไม่สด ต้องตรวจยอดใหม่",
  DAILY_BASELINE_REQUIRED: "ต้องยืนยันยอดต้นวันก่อนตรวจความเสี่ยง",
  UNRESOLVED_ORDER: "มีคำสั่งที่ยังยืนยันผลไม่ได้ ต้องตรวจจากตลาด",
};
function validStatus(value: unknown): value is Status {
  if (!value || typeof value !== "object") return false;
  const s = value as Status;
  return Object.hasOwn(labels, s.state) && typeof s.armed === "boolean" && Array.isArray(s.reasons) &&
    s.reasons.every(r => typeof r === "string") && s.armed === (s.state === "ARMED") &&
    (!s.armed || s.reasons.length === 0);
}
export async function readLiveStatus(origin: string, fetcher: typeof fetch = fetch, signal?: AbortSignal): Promise<Connection> {
  if (origin !== ownerOrigin) return { kind: "https-required" };
  try {
    const response = await requestJson(`${executor}/live/status`, { credentials: "include", signal }, fetcher);
    if (response.status === 401) return { kind: "pair-required" };
    if (response.status !== 200) return { kind: "unavailable" };
    const status: unknown = response.data;
    return validStatus(status) ? { kind: "connected", status } : { kind: "unavailable" };
  } catch { return { kind: "unavailable" }; }
}
// No arm or order-submission action in this readiness client. Never retry a mutation.
export async function postOwnerAction(origin: string, action: "pair" | "stop" | "logout", code?: string,
  fetcher: typeof fetch = fetch, signal?: AbortSignal, rememberBrowser = false): Promise<{ ok: boolean }> {
  if (origin !== ownerOrigin || !["pair", "stop", "logout"].includes(action)) return { ok: false };
  try {
    const csrf = await requestJson(`${executor}/owner/csrf`, { credentials: "include", signal }, fetcher);
    if (csrf.status !== 200) return { ok: false };
    const token: unknown = (csrf.data as { token?: unknown })?.token;
    if (typeof token !== "string" || !token) return { ok: false };
    const response = await requestJson(`${executor}/${action === "stop" ? "live/stop" : `owner/${action}`}`, {
      method: "POST", credentials: "include", cache: "no-store", signal,
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
      body: action === "pair" ? JSON.stringify({ code, rememberBrowser }) : undefined,
    }, fetcher);
    return { ok: response.status >= 200 && response.status < 300 };
  } catch { return { ok: false }; }
}

export function LiveReadinessView({ state, code, busy, rememberBrowser = true, accountOnly = false, onCodeChange, onRememberBrowserChange, onPair }: {
  state: Connection; code?: string; busy?: boolean; rememberBrowser?: boolean; onCodeChange?: (value: string) => void;
  onRememberBrowserChange?: (value: boolean) => void; onPair?: () => void; accountOnly?: boolean;
}) {
  const title = state.kind === "connected" ? labels[state.status.state] :
    state.kind === "loading" ? "กำลังตรวจระบบเงินจริง" : state.kind === "pair-required" ? "ยืนยันเจ้าของก่อนดูบัญชี" :
    state.kind === "https-required" ? "ต้องเตรียมการเชื่อมต่อ HTTPS ก่อน" : "ติดต่อระบบเงินจริงไม่ได้";
  return <section className="live-readiness" aria-labelledby="live-heading">
    <h2 id="live-heading">{accountOnly ? "เชื่อมบัญชี Bitkub" : "สถานะการเทรด"}</h2>
    <p className="live-state" role="status">{title}</p>
    <p>{accountOnly ? "ยืนยันเจ้าของเพื่ออ่านยอดจาก Bitkub ในเบราว์เซอร์นี้" : state.kind === "connected" && state.status.state === "ARMED" ? "ระบบรายงานสิทธิ์ส่งคำสั่งแล้ว ดูผลที่ยืนยันได้จากรายการคำสั่ง" : state.kind === "connected" && state.status.state !== "DISABLED" ? "ตรวจยอดและวงเงินด้านล่างก่อนเปิดสิทธิ์ CEO การอ่านบัญชีหรือบันทึกวงเงินยังไม่เริ่มซื้อขาย" : "ยังเปิดซื้อขายเงินจริงไม่ได้ ขั้นนี้เชื่อม Bitkub เพื่อดูยอดหลังยืนยันเจ้าของ"}</p>
    {state.kind === "https-required" && <p>Live ต้องใช้ https://localhost:3000 และบริการเจ้าของบัญชีที่เชื่อถือได้ ยังไม่ต้องป้อนข้อมูลบัญชีหรือโอนเงิน</p>}
    {state.kind === "unavailable" && <p>ยังยืนยันสถานะและยอดเงินไม่ได้ ตรวจบริการเจ้าของบัญชีและใบรับรอง HTTPS แล้วลองตรวจอีกครั้ง อย่าถือว่าการขาดการเชื่อมต่อคือคำสั่งหยุดสำเร็จ</p>}
    {state.kind === "pair-required" && <>
      <p>วางรหัสจากหน้าต่าง Quantara - Bitkub pairing ภายใน 5 นาที</p>
      <form className="live-owner-form" onSubmit={event => { event.preventDefault(); onPair?.(); }}>
        <label htmlFor="owner-code">รหัสจับคู่เจ้าของบัญชี</label>
        <input id="owner-code" type="password" autoComplete="off" value={code ?? ""} onChange={event => onCodeChange?.(event.target.value)} required maxLength={64} disabled={busy} aria-describedby="owner-code-help" />
        <p id="owner-code-help" className="live-form-help">รหัสนี้ใช้เข้าแอปบนเครื่องนี้ ไม่ใช่ API key หรือ Secret</p>
        <label className="live-remember-browser"><input type="checkbox" checked={rememberBrowser} onChange={event => onRememberBrowserChange?.(event.target.checked)} disabled={busy} />จำเบราว์เซอร์นี้สำหรับดูบัญชี 30 วัน</label>
        <p className="live-form-help">ใช้สำหรับดูข้อมูลเท่านั้น การควบคุมเงินต้องยืนยันใหม่ และควรยกเลิกบนเครื่องที่ใช้ร่วมกัน</p>
        <div className="live-owner-form-actions">
          <button disabled={busy || !(code ?? "").trim()} type="submit">{busy ? "กำลังยืนยัน…" : "ยืนยันเจ้าของ"}</button>
        </div>
      </form>
    </>}
    {state.kind === "connected" && state.status.reasons.length > 0 && <ul>{state.status.reasons.map((r, i) =>
      <li key={`${r}-${i}`}>{reasons[r] ?? "มีเงื่อนไขที่ต้องตรวจสอบจากระบบเจ้าของบัญชี"}</li>)}</ul>}
    {!accountOnly && <details className="live-release-details">
      <summary>รายละเอียดความพร้อมก่อนเริ่มใช้เงินจริง</summary>
      <ol className="live-checklist">
        <li><strong>เชื่อมตลาดและตรวจบัญชี</strong><span>ตรวจจำนวนเหรียญ ค่าธรรมเนียม และผลคำสั่งจริงให้ตรงกัน รวมกรณีการเชื่อมต่อขาด</span></li>
        <li><strong>แยกสิทธิ์และผูกบัญชีเจ้าของ</strong><span>AI อ่านข้อมูลและเสนอคำสั่งได้ แต่เข้าถึงกุญแจซื้อขายหรือแก้บัญชีไม่ได้</span></li>
        <li><strong>เจ้าของตั้งวงเงินและเปิดใช้งาน</strong><span>ต้องผ่านการตรวจความพร้อมก่อน ไม่เปิดอัตโนมัติจากการสลับโหมด</span></li>
      </ol>
      <p className="live-separation">การอ่านบัญชีนี้แยกจากสิทธิ์ควบคุมเงิน เจ้าของต้องยืนยันใหม่ก่อนการควบคุมใด ๆ</p>
    </details>}
  </section>;
}

type Observation = { state: Connection; wallet?: BitkubWallet; account?: LiveAccount };

// Commit a verified observation together. A pending read never clears a fresh balance.
export async function readLiveObservation(origin: string, fetcher: typeof fetch = fetch, signal?: AbortSignal): Promise<Observation> {
  const state = await readLiveStatus(origin, fetcher, signal);
  if (state.kind !== "connected") return { state };
  return state.status.state === "DISABLED"
    ? { state, wallet: await readBitkubWallet(origin, fetcher, signal) }
    : { state, account: await readLiveAccount(origin, state.status, fetcher, signal) };
}

type LiveWorkspace = Observation & {
  refreshing: boolean; busy: boolean; code: string; rememberBrowser: boolean; message: string;
  setCode: (code: string) => void; setRememberBrowser: (remember: boolean) => void;
  refresh: () => void; act: (action: "pair" | "stop" | "logout") => Promise<void>;
};
const LiveContext = createContext<LiveWorkspace | null>(null);
export function useLiveAccount() {
  const value = useContext(LiveContext);
  if (!value) throw new Error("LiveAccountProvider is required");
  return value;
}

export function LiveAccountProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<Connection>({ kind: "loading" });
  const [code, setCode] = useState("");
  const [rememberBrowser, setRememberBrowser] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [revision, setRevision] = useState(0);
  const [account, setAccount] = useState<LiveAccount>();
  const [wallet, setWallet] = useState<BitkubWallet>();
  const [refreshing, setRefreshing] = useState(true);
  const actionAbort = useRef<AbortController | null>(null);
  useEffect(() => () => actionAbort.current?.abort(), []);
  useEffect(() => {
    if (busy) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    async function refresh() {
      // Keep current verified data while the next observation is pending; expiry below still runs.
      setRefreshing(true);
      const next = await readLiveObservation(window.location.origin, fetch, controller.signal);
      if (controller.signal.aborted) return;
      setState(next.state);
      setAccount(next.account);
      setWallet(next.wallet);
      setRefreshing(false);
      if (next.state.kind !== "https-required") timer = setTimeout(refresh, 10000);
    }
    void refresh();
    return () => { controller.abort(); clearTimeout(timer); };
  }, [revision, busy]);
  useEffect(() => {
    if (!wallet) return;
    const expiry = setTimeout(() => setWallet(current => current === wallet ? undefined : current), Math.max(0, Date.parse(wallet.observedAt) + 30000 - Date.now()));
    return () => clearTimeout(expiry);
  }, [wallet]);
  useEffect(() => {
    if (!account) return;
    const expiry = setTimeout(() => setAccount(current => current === account ? undefined : current), Math.max(0, Date.parse(account.snapshot.observedAt) + 30000 - Date.now()));
    return () => clearTimeout(expiry);
  }, [account]);
  async function act(action: "pair" | "stop" | "logout") {
    if (actionAbort.current) return;
    const controller = new AbortController();
    actionAbort.current = controller;
    setBusy(true); setMessage("");
    setAccount(undefined);
    setWallet(undefined);
    const pairingCode = code; setCode("");
    const result = await postOwnerAction(window.location.origin, action, pairingCode, fetch, controller.signal, rememberBrowser);
    actionAbort.current = null;
    if (controller.signal.aborted) return;
    setBusy(false);
    // Clear cached authority after any mutation, including an ambiguous outcome.
    setState({ kind: "loading" });
    setMessage(result.ok ? action === "stop" ? "บันทึกคำสั่งหยุดแล้ว ไม่ได้ขายสินทรัพย์หรือยกเลิกคำสั่งค้างอัตโนมัติ" :
      action === "pair" ? (rememberBrowser ? "จับคู่แล้ว จดจำเบราว์เซอร์นี้เพื่อดูบัญชีได้ 30 วัน การจับคู่ไม่เปิดการเทรด" : "จับคู่แล้ว การจับคู่ไม่เปิดการเทรด") : "ออกจากระบบแล้ว การออกจากระบบไม่ใช่คำสั่งหยุดเทรด" :
      action === "stop" ? "ยังยืนยันการหยุดไม่ได้ ตรวจสถานะจากบริการเจ้าของบัญชี ห้ามถือว่าหยุดสำเร็จ" : "ดำเนินการไม่สำเร็จ ตรวจการเชื่อมต่อหรือขอรหัสจับคู่ใหม่ ไม่มีการลองซ้ำอัตโนมัติ");
    setRevision(r => r + 1);
  }
  return <LiveContext.Provider value={{ state, account, wallet, refreshing, busy, code, rememberBrowser, message,
    setCode, setRememberBrowser, refresh: () => setRevision(r => r + 1), act }}>
    {children}
    <CompanyChat><LiveTeamSummary /></CompanyChat>
  </LiveContext.Provider>;
}

function OwnerAccess({ accountOnly = false }: { accountOnly?: boolean }) {
  const { state, code, busy, rememberBrowser, setCode, setRememberBrowser, act } = useLiveAccount();
  return <LiveReadinessView state={busy ? { kind: "loading" } : state} code={code} busy={busy}
    rememberBrowser={rememberBrowser} accountOnly={accountOnly} onCodeChange={setCode} onRememberBrowserChange={setRememberBrowser}
    onPair={() => void act("pair")} />;
}

export function LiveConnectionBadge() {
  const { state, refreshing } = useLiveAccount();
  const text = state.kind === "connected" ? "เชื่อมบริการบัญชีแล้ว" : state.kind === "pair-required" ? "รอยืนยันเจ้าของ" :
    state.kind === "loading" ? "กำลังเชื่อมต่อ" : "ขาดการเชื่อมต่อ";
  return <span className="live-connection"><span className="connection-dot" aria-hidden="true" />{text}
    {refreshing && state.kind === "connected" && <span className="refresh-indicator" aria-label="กำลังตรวจข้อมูลล่าสุด" />}</span>;
}

function LiveActions({ logout = false }: { logout?: boolean }) {
  const { state, busy, refreshing, refresh, act, message } = useLiveAccount();
  return <>
    <div className="live-actions">
      <button type="button" disabled={busy || refreshing} onClick={refresh}>{refreshing ? "กำลังตรวจข้อมูล…" : "ตรวจข้อมูลล่าสุด"}</button>
      {state.kind === "connected" && <>
        {state.status.state !== "DISABLED" && <button type="button" disabled={busy} onClick={() => void act("stop")}>หยุดส่งคำสั่งใหม่</button>}
        {logout && <button type="button" disabled={busy} onClick={() => void act("logout")}>ออกจากระบบและลืมเบราว์เซอร์นี้</button>}
      </>}
    </div>
    {message && <p role="status">{message}</p>}
  </>;
}

export function LiveAccountPanel() {
  const { state, wallet, account, refreshing, busy } = useLiveAccount();
  return <div className="account-workspace">
    {state.kind === "connected" && !busy ? <>
      {wallet ? <BitkubWalletView wallet={wallet} /> : account ? <LiveAccountView account={account} /> :
        <div className="workspace-empty"><h2>{refreshing ? "กำลังอ่านบัญชี" : "ยังยืนยันยอดล่าสุดไม่ได้"}</h2>
          <p>{refreshing ? "กำลังตรวจยอดจาก Bitkub ข้อมูลที่เกิน 30 วินาทีจะไม่แสดงเป็นยอดปัจจุบัน" : "ตรวจการเชื่อมต่อกับ Bitkub แล้วลองตรวจข้อมูลอีกครั้ง"}</p></div>}
    </> : <OwnerAccess accountOnly />}
    <LiveActions logout />
  </div>;
}

export function LiveTradingPanel() {
  const { state, account, refresh } = useLiveAccount();
  return <div className="trading-workspace"><OwnerAccess />
    {state.kind === "connected" && <LiveOwnerSetup onChanged={refresh} />}
    {state.kind === "connected" && <section className="trading-orders"><h2>รายการคำสั่ง</h2>
      {account ? <LiveOrdersView account={account} /> : <p>ยังไม่มีบัญชีสำหรับส่งคำสั่งที่ตรวจยอดแล้ว จึงยังแสดงรายการคำสั่ง Live ไม่ได้</p>}
    </section>}
    <LiveActions />
  </div>;
}

export function LiveOfficeWorkspace() {
  const { state, wallet, account } = useLiveAccount();
  const caption = state.kind === "connected" ? labels[state.status.state] : state.kind === "pair-required" ? "ยืนยันเจ้าของเพื่อดูสถานะบัญชี" : "ยังยืนยันสถานะบริษัทล่าสุดไม่ได้";
  const cash = wallet?.balances.find(b => b.currency === "THB")?.available ??
    (account ? account.snapshot.cashBalance - account.snapshot.cashReserved : undefined);
  return <div className="office-workspace"><LiveOffice />
    <div className="office-overview">
      <section><h2>บริษัทตอนนี้</h2><p className="office-state">{caption}</p>
        <p>CEO ประชุมจากยอดที่ตรวจแล้วหลังเจ้าของเปิดสิทธิ์ อ่านข้อสรุปและเหตุผลย้อนหลังได้ในห้องทีม</p>
        <Link href="/trading" className="workspace-link">ดูสถานะการเทรด<Arrow /></Link>
      </section>
      <section><h2>บัญชี Bitkub</h2><p className="office-balance">{cash === undefined ? "ยังยืนยันยอดไม่ได้" : new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" }).format(cash)}</p>
        <p>{cash === undefined ? "ยืนยันเจ้าของและตรวจยอดล่าสุดที่หน้าบัญชี" : "THB ที่ใช้ได้ · การอ่านยอดไม่เปิดสิทธิ์ซื้อขาย"}</p>
        <Link href="/account" className="workspace-link">ดูบัญชีและสินทรัพย์<Arrow /></Link>
      </section>
    </div>
  </div>;
}
function Arrow() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14m-5-5 5 5-5 5" /></svg>; }

function LiveTeamSummary() {
  const { state, wallet, account } = useLiveAccount();
  const title = state.kind === "connected" ? labels[state.status.state] : "ยังยืนยันสถานะ Live ไม่ได้";
  const observation = wallet?.observedAt ?? account?.snapshot.observedAt;
  return <div className="live-team-summary">
    <section className="team-conclusion"><h3>ข้อสรุปล่าสุด</h3><p className="team-conclusion-text">{title}</p>
      {state.kind === "connected" && state.status.reasons.length > 0 && <><h4>เหตุผลจากระบบ</h4><ul>{state.status.reasons.map((r, i) => <li key={`${r}-${i}`}>{reasons[r] ?? "มีเงื่อนไขที่ต้องตรวจจากบริการบัญชี"}</li>)}</ul></>}
      {observation && <p className="data-freshness">อ่านบัญชีเมื่อ {new Date(observation).toLocaleTimeString("th-TH", { timeZone: "Asia/Bangkok" })}</p>}
    </section>
    <LiveTeamReports enabled={state.kind === "connected" && state.status.state !== "DISABLED"} />
    {state.kind === "connected" && state.status.state === "DISABLED" && <section className="team-report-state"><h3>รายงานประชุม Live</h3><p>ระบบประชุมเริ่มเมื่อบัญชีผ่านการตรวจและเจ้าของเปิดสิทธิ์แล้ว ขณะนี้ยังไม่มีรายงานประชุม Live ที่ยืนยันได้</p></section>}
  </div>;
}

export function LiveAccountView({ account }: { account: LiveAccount }) {
  const cash = (n: number) => new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" }).format(n);
  const s = account.snapshot;
  return <section className="money-section" aria-label="บัญชี Live ที่ตรวจยอดแล้ว">
    <h2>บัญชีที่ตรวจยอดแล้ว</h2>
    <p className="data-freshness">ตรวจจากตลาด {new Date(s.observedAt).toLocaleString("th-TH", { timeZone: "Asia/Bangkok" })} · การตรวจยอดไม่เปิดสิทธิ์ซื้อขาย</p>
    <div className="cfo-metrics portfolio-metrics"><div><span>มูลค่าบัญชี</span><strong>{cash(s.equity)}</strong></div><div><span>เงินสดที่ใช้ได้</span><strong>{cash(s.cashBalance - s.cashReserved)}</strong></div><div><span>เงินที่กันไว้</span><strong>{cash(s.cashReserved)}</strong></div></div>
    <h3>สินทรัพย์ที่ถืออยู่</h3>
    {s.positions.length ? s.positions.map(p => <div className="position-row" key={p.symbol}><div><strong>{p.symbol}</strong><span>ถือ {p.quantity} · กันขาย {p.reservedQuantity}</span></div><div><strong>{cash(p.quantity * p.marketPrice)}</strong><span>ประเมินด้วยราคาเสนอซื้อ</span></div></div>) : <p>ไม่มีสินทรัพย์ที่ถืออยู่ในบัญชีที่ตรวจยอดนี้</p>}
    <details className="transaction-details"><summary>วงเงินที่เจ้าของกำหนด</summary><dl className="money-breakdown"><div><dt>เงินที่อนุญาตให้ลงทุน</dt><dd>{cash(account.mandate.capitalAllocation)}</dd></div><div><dt>สูงสุดต่อคำสั่ง</dt><dd>{cash(account.mandate.maximumOrderNotional)}</dd></div><div><dt>ขาดทุนสูงสุดต่อวัน</dt><dd>{cash(account.mandate.maximumDailyLoss)}</dd></div></dl></details>
  </section>;
}

export function LiveOrdersView({ account }: { account: LiveAccount }) {
  const cash = (n: number) => new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" }).format(n);
  return account.orders.length ? <div className="live-order-list">{account.orders.slice().reverse().map(o =>
    <div className="position-row" key={o.intentId}><div><strong>{o.symbol} · {o.side === "Buy" ? "ซื้อ" : "ขาย"}</strong>
      <span>{orderLabels[o.state]} · จับคู่แล้ว {o.filledQuantity}</span></div><div><span>เงินกันไว้ {cash(o.reservedCash)}</span>
      <span>จำนวนกันขาย {o.reservedQuantity}</span></div></div>)}</div> : <p>ยังไม่มีคำสั่งที่บันทึกไว้ในบัญชีนี้</p>;
}

export function BitkubWalletView({ wallet }: { wallet: BitkubWallet }) {
  const cash = (n: number) => new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" }).format(n);
  const thb = wallet.balances.find(b => b.currency === "THB")!;
  const coins = wallet.balances.filter(b => b.currency !== "THB" && b.total > 0);
  return <section className="money-section" aria-label="ยอดจาก Bitkub อ่านข้อมูลเท่านั้น"><h2>ยอดจาก Bitkub · อ่านข้อมูลเท่านั้น</h2>
    <p className="data-freshness">อ่านเมื่อ {new Date(wallet.observedAt).toLocaleString("th-TH", { timeZone: "Asia/Bangkok" })}</p>
    <div className="cfo-metrics portfolio-metrics"><div><span>THB ใช้ได้</span><strong>{cash(thb.available)}</strong></div><div><span>THB กันไว้</span><strong>{cash(thb.reserved)}</strong></div><div><span>THB รวม</span><strong>{cash(thb.total)}</strong></div></div>
    <h3>เหรียญที่มียอดคงเหลือ</h3>
    {coins.length ? coins.map(b => <div className="position-row" key={b.currency}><div><strong>{b.currency}</strong><span>จำนวนรวม</span></div><div><strong>{b.total}</strong><span>ไม่มีการประเมินมูลค่า</span></div></div>) : <p>ไม่มีเหรียญที่มียอดคงเหลือในการตรวจครั้งนี้</p>}
    <p>อ่านยอดได้แล้ว ยังไม่ยืนยันบัญชีสำหรับส่งคำสั่งหรือเปิดการเทรด</p></section>;
}
