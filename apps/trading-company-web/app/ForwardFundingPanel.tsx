import { useEffect, useRef, useState } from "react";
import { requestJson } from "./requestJson";
import { validCompanyStatus, validFundingStatus } from "./forwardStatus";
import type { CompanyStatus, FundingStatus, Portfolio, SavedMeeting } from "./forwardStatus";

const money = new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" });
const value = (n: number | null | undefined) => n == null ? "ยังไม่มีข้อมูล" : money.format(n);
export const thaiDate = (date?: string | null, time = false) => !date ? "ยังไม่มีข้อมูล" : new Intl.DateTimeFormat("th-TH", { day: "numeric", month: "short", year: "numeric", timeZone: "Asia/Bangkok", ...(time ? { hour: "2-digit", minute: "2-digit" } : {}) }).format(new Date(date));
const nativeMoney = (currency: string, amount: number) => new Intl.NumberFormat("th-TH", { style: "currency", currency }).format(amount);
export const companyLabels: Record<string, string> = {
  PAUSED: "พักการทำงานแล้ว", AWAITING_INVESTMENT_BUDGET: "รอเงินสำหรับลงทุน", MODEL_DISABLED: "AI ยังไม่เปิดทำงาน",
  RESEARCHING: "ทีมกำลังศึกษาตลาด", MEETING: "ทีมกำลังประชุม", WAITING_FOR_WORKDAY: "รอรอบทำงานถัดไป",
  AWAITING_EXECUTION_QUOTE: "รอราคาสำหรับคำสั่งจำลอง", COMPLETED: "จบรอบวิเคราะห์แล้ว", NO_EVIDENCE: "ข้อมูลยังไม่พอสำหรับตัดสินใจ",
  RECONCILIATION_REQUIRED: "หยุดรอตรวจบัญชี", TOKEN_BUDGET: "โควตารอบนี้ครบแล้ว", MODEL_CALL_BUDGET: "โควตารอบนี้ครบแล้ว",
  TIMEOUT: "AI ตอบไม่ทัน", PROVIDER_FAILURE: "ติดต่อ AI ไม่สำเร็จ", MODEL_UNAVAILABLE: "โมเดลยังไม่พร้อม", CREDITS_UNAVAILABLE: "โควตา AI ไม่พร้อม",
  DISABLED: "ระบบยังไม่เปิดทำงาน", NOT_STARTED: "กำลังเริ่มระบบ", ALREADY_ATTEMPTED: "บันทึกรอบวันนี้แล้ว",
  INVALID_FINAL: "ข้อเสนอไม่ผ่านการตรวจสอบ", INCOMPLETE: "รอบวิเคราะห์ยังไม่ครบ", FORMAT_FAILURE: "คำตอบ AI ไม่ผ่านการตรวจสอบ",
  INVALID_ACCOUNT_CONTEXT: "บริบทบัญชีไม่ตรงกับรอบบริษัท",
};
export const roleLabels: Record<string, string> = {
  ceo: "ผู้บริหาร", "chief-of-staff": "ผู้ประสานงาน", "market-research": "วิจัยตลาด", "security-analysis": "วิเคราะห์สินทรัพย์",
  "quant-research": "วิเคราะห์เชิงปริมาณ", "portfolio-initial": "ผู้จัดการพอร์ต · ข้อเสนอแรก", "risk-critic": "ตรวจความเสี่ยง", "portfolio-final": "ผู้จัดการพอร์ต · ข้อสรุป",
};

export function SavedCompanyDay({ meeting }: { meeting?: SavedMeeting | null }) {
  if (!meeting) return <section className="saved-company-day"><h3>ผลการทำงานวันนี้</h3><p>ยังไม่มีรอบที่บันทึกไว้สำหรับวันนี้</p></section>;
  const summary = meeting.summary, completed = meeting.status === "COMPLETED";
  return <section className="saved-company-day" aria-label="ผลการทำงานวันนี้">
    <h3>ผลการทำงาน · {thaiDate(meeting.businessDate)}</h3>
    <p>{companyLabels[meeting.status] ?? "รอบทำงานต้องตรวจสอบ"} · การจบประชุมไม่ได้หมายความว่าซื้อขายสำเร็จ</p>
    {summary ? <p className="meeting-usage">เรียก AI {summary.modelCalls} ครั้ง · ใช้ {summary.inputTokens + summary.outputTokens} tokens</p> : <p>รอบเดิมไม่มีรายงานที่บันทึกไว้ ไม่สามารถสร้างข้อมูลย้อนหลังได้</p>}
    {completed && Boolean(summary?.reports.length) && <details className="saved-ai-reports"><summary>อ่านรายงานทีม ({summary!.reports.length})</summary>
      <ol>{summary!.reports.map(report => <li key={report.artifactId}><strong>{roleLabels[report.role] ?? report.role}</strong><p>{report.summary}</p><small>ข้อมูลถึง {thaiDate(report.dataCutoff)} · {report.providerModel}</small></li>)}</ol>
    </details>}
    {!completed && summary && <p>รอบนี้ยังไม่ผ่านครบทุกขั้นตอน ไม่มีรายงานที่รับรองว่าการวิเคราะห์สำเร็จ</p>}
  </section>;
}

export async function readForwardEndpoint<T>(url: string, fetcher: typeof fetch = fetch, signal?: AbortSignal,
  validate: (data: unknown) => data is T = validFundingStatus as (data: unknown) => data is T): Promise<{ ok: true; data: T } | { ok: false; error: string }> {
  try {
    const response = await requestJson(url, { signal }, fetcher);
    if (response.status < 200 || response.status >= 300) return { ok: false, error: `HTTP ${response.status}` };
    return validate(response.data) ? { ok: true, data: response.data } : { ok: false, error: "invalid response" };
  } catch { return { ok: false, error: signal?.aborted ? "aborted" : "unavailable" }; }
}

export async function setSandboxPaused(base: string, paused: boolean, fetcher: typeof fetch = fetch, signal?: AbortSignal): Promise<boolean> {
  try {
    const nonce = await requestJson(`${base}/sandbox/control`, { signal }, fetcher);
    const token = (nonce.data as { token?: unknown })?.token;
    if (nonce.status !== 200 || typeof token !== "string" || !/^[a-f0-9]{64}$/i.test(token)) return false;
    const result = await requestJson(`${base}/sandbox/control`, { signal, method: "POST", headers: { "Content-Type": "application/json", "X-Sandbox-Control": token }, body: JSON.stringify({ paused }) }, fetcher);
    return result.status === 200 && (result.data as { paused?: unknown })?.paused === paused;
  } catch { return false; }
}

function Metric({ label, amount, fallback = "ยังไม่มีข้อมูล" }: { label: string; amount: number | undefined | null; fallback?: string }) {
  return <div><span>{label}</span><strong>{amount == null ? fallback : value(amount)}</strong></div>;
}
function ScrollTable({ children }: { children: React.ReactNode }) {
  return <div className="table-section"><p className="table-hint">เลื่อนตารางเพื่อดูรายละเอียดเพิ่มเติม</p><div className="cfo-table-wrap" tabIndex={0} role="region" aria-label="ตารางรายละเอียด เลื่อนแนวนอนได้">{children}</div></div>;
}
function PortfolioDetails({ portfolio }: { portfolio: Portfolio }) {
  const positions = portfolio.positions ?? [], pending = portfolio.pendingOrders ?? [], receipts = portfolio.receipts ?? [];
  return <section className="money-section" aria-label="พอร์ตลงทุนจำลอง">
    <header className="section-heading"><div><h2>พอร์ตลงทุน</h2><p>ยอดจากบัญชีจำลองที่บันทึกไว้</p></div></header>
    <div className="cfo-metrics portfolio-metrics"><Metric label="มูลค่าพอร์ต" amount={portfolio.equity} /><Metric label="เงินสดในพอร์ต" amount={portfolio.cash} /><Metric label="กำไร / ขาดทุนรวม" amount={portfolio.totalPnl} /></div>
    <details className="company-detail"><summary>รายละเอียดมูลค่าพอร์ต</summary><dl className="money-breakdown"><div><dt>เงินลงทุนสะสม</dt><dd>{value(portfolio.totalContributed)}</dd></div><div><dt>กำไร / ขาดทุนที่รับรู้แล้ว</dt><dd>{value(portfolio.realizedPnl)}</dd></div><div><dt>มูลค่าที่ลดลงจากจุดสูงสุด</dt><dd>{value(portfolio.drawdown)}</dd></div>{portfolio.initialCapital != null && <div><dt>ทุนจำลองเริ่มต้น</dt><dd>{value(portfolio.initialCapital)}</dd></div>}</dl></details>
    <h3>สินทรัพย์ที่ถืออยู่ <span className="section-count">{positions.length} รายการ</span></h3>
    {positions.length ? <div className="position-list">{positions.map(p => <article className="position-row" key={p.symbol.value}><div><strong>{p.symbol.value}</strong><span>{p.quantity} หน่วย</span></div><div><strong>{value(p.marketValue)}</strong><span>กำไร / ขาดทุน {value(p.unrealizedPnl)}</span></div><small>ราคาเฉลี่ย {value(p.averagePrice)} · ราคาประเมิน {value(p.marketPrice)}</small></article>)}</div> : <p className="empty">ยังไม่มีสินทรัพย์ในพอร์ต เงินลงทุนอยู่ในรูปเงินสด</p>}
    <h3>คำสั่งที่รอดำเนินการ <span className="section-count">{pending.length} รายการ</span></h3>
    {pending.length ? <div className="position-list">{pending.map(o => <article className="position-row" key={o.id}><div><strong>{o.symbol}</strong><span>{o.side === 0 ? "ซื้อ" : o.side === 1 ? "ขาย" : "ยังยืนยันไม่ได้"} · {o.quantity} หน่วย</span></div><div><span>รอราคาใหม่หลังตัดสินใจ</span><span>{thaiDate(o.decisionAt, true)}</span></div><details><summary>รหัสคำสั่ง</summary><small>{o.id}</small></details></article>)}</div> : <p className="empty">ไม่มีคำสั่งค้าง</p>}
    <details className="transaction-details"><summary>ผลคำสั่งและรายการเงินเข้า ({receipts.length + (portfolio.credits?.length ?? 0)})</summary>
      {receipts.length ? <div className="position-list">{receipts.slice().reverse().map(r => <article className="position-row" key={r.orderId}><div><strong>{r.quote?.symbol ?? "ยังไม่มีข้อมูล"}</strong><span>{r.status === "RISK_REJECTED" ? "ไม่ผ่านการตรวจความเสี่ยง" : r.status === "PAPER_FILLED" ? "บันทึกซื้อขายจำลองแล้ว" : "ยังยืนยันผลไม่ได้"}</span></div><div><strong>{r.quote ? nativeMoney(r.quote.currency, r.quote.nativePrice) : "ยังไม่มีข้อมูล"}</strong><span>{r.quote?.source}</span></div><details><summary>รายละเอียดคำสั่ง</summary><p>รหัส {r.orderId}</p><p>อัตราแปลงเป็นบาท {r.quote ? value(r.quote.thbPerUnit) : "ยังไม่มีข้อมูล"}</p><p>{r.violations.length ? `เงื่อนไข: ${r.violations.join(", ")}` : "ไม่มีข้อจำกัดที่รายงาน"}</p></details></article>)}</div> : <p className="empty">ยังไม่มีผลคำสั่งซื้อขาย</p>}
      {Boolean(portfolio.credits?.length) && <ScrollTable><table className="cfo-table"><caption>รายการเงินเข้าพอร์ตจากเงินเดือนจำลอง</caption><thead><tr><th>รอบเงินเดือน</th><th>จำนวนเงิน</th><th>บันทึกเมื่อ</th></tr></thead><tbody>{portfolio.credits!.map(c => <tr key={`${c.salaryDate}-${c.creditedAt}`}><td>{thaiDate(c.salaryDate)}</td><td>{value(c.amount)}</td><td>{thaiDate(c.creditedAt, true)}</td></tr>)}</tbody></table></ScrollTable>}
    </details>
  </section>;
}

function CompanyDetails({ company, companyError }: { company?: CompanyStatus; companyError?: string }) {
  return <details className="company-detail"><summary>รายละเอียดระบบและตลาดที่รองรับ</summary>
    {companyError || !company ? <p>ยังยืนยันสถานะระบบไม่ได้ กำลังเชื่อมต่อใหม่</p> : <>
      <p>AI: {company.modelEnabled === true ? `เปิดใช้งาน (${company.model ?? "ไม่ระบุโมเดล"})` : company.modelEnabled === false ? "ยังไม่เปิด" : "ยังยืนยันไม่ได้"} · ประชุมล่าสุด: {thaiDate(company.lastMeetingDate)}</p>
      <p>ตลาดที่รองรับ: {company.supportedSymbols?.join(", ") || "ยังไม่มีข้อมูล"}</p>
      <p>รอบ AI หลัง 09:00 น. เวลาไทย: Crypto ทุกวัน หุ้นจันทร์–ศุกร์ ต้องผ่านเกณฑ์เงินสำรองก่อน</p>
      {company.publicCryptoQuotes && <p>ใช้ราคาสาธารณะ Bitkub สำหรับ Crypto · จำลองค่าธรรมเนียม 0.25% ต่อข้าง รวมส่วนต่างราคาและข้อจำกัดปริมาณซื้อขาย{company.equityQuotesConfigured === false ? " · หุ้นยังไม่เปิด ต้องตั้งค่าข้อมูลราคาหุ้นก่อน" : ""}</p>}
      {company.quoteCredentialsConfigured === false && <p>ยังไม่ได้ตั้งค่าข้อมูลราคาสำหรับคำสั่งจำลอง</p>}
      <p>สถานะ: {company.status} · RiskEngine ตรวจคำสั่งสุดท้าย · ไม่มีการส่งคำสั่งเงินจริง</p>
      {company.error && <p>หยุดรอตรวจบันทึกบัญชีและรอบทำงานก่อนเริ่มใหม่</p>}
    </>}
  </details>;
}

export function ForwardFundingView({ data, error, company, companyError, mode = "operations", updatedAt, companyUpdatedAt, controls }: {
  data?: FundingStatus; error?: string; company?: CompanyStatus; companyError?: string; mode?: "home" | "operations";
  updatedAt?: string; companyUpdatedAt?: string; controls?: React.ReactNode;
}) {
  const account = data?.account, unavailable = Boolean(error || companyError || !company);
  const title = unavailable ? "กำลังตรวจสถานะบริษัท" : company?.error ? "หยุดรอตรวจบัญชี" : company?.paused ? company.busy ? "พักรอบถัดไปแล้ว" : "พักการทำงานแล้ว" : company?.modelEnabled === false ? "AI ยังไม่เปิดทำงาน" : companyLabels[company?.status ?? ""] ?? "รอบทำงานต้องตรวจสอบ";
  const message = error || companyError ? "ยังยืนยันข้อมูลล่าสุดไม่ได้ กำลังเชื่อมต่อใหม่อัตโนมัติ" : !data ? "กำลังโหลดบัญชีจำลอง…" : data.status === "DISABLED" ? "ระบบเงินเดือนจำลองยังไม่เปิดทำงาน" : !account ? "กำลังเตรียมบัญชีจำลอง" : !company ? "รอยืนยันสถานะ AI" : company.error ? "ต้องตรวจบันทึกบัญชีและรอบทำงานก่อนเริ่มใหม่" : company.paused ? "พักรอบใหม่และคำสั่งจำลอง งานที่เริ่มไปแล้วอาจยังทำต่อ เงินเดือนจำลองยังบันทึกตามแผน" : !company.modelEnabled ? "เงินเดือนจำลองอาจยังถูกบันทึก แต่ AI จะยังไม่วิเคราะห์หรือเสนอคำสั่ง" : !data.portfolio?.totalContributed ? account.postings.length ? "กำลังสะสมเงินสำรอง เมื่อผ่านเกณฑ์จึงจัดเงินลงทุน" : `เงินเดือนจำลองรอบแรก ${thaiDate(account.nextPayday)}` : company.status === "WAITING_FOR_WORKDAY" ? "ยังไม่ถึงรอบวิเคราะห์ เริ่มอัตโนมัติหลัง 09:00 น. เวลาไทย วันละหนึ่งรอบ" : company.status === "AWAITING_EXECUTION_QUOTE" ? "คำสั่งยังไม่ถูกซื้อขาย ต้องได้ราคาใหม่หลังตัดสินใจก่อน" : company.status === "COMPLETED" ? "ดูผลตัดสินใจและรายการซื้อขาย การไม่ซื้อขายก็เป็นผลลัพธ์ได้" : "บริษัททำงานตามรอบโดยอัตโนมัติ";
  const finalReport = !companyError && company?.lastMeeting?.status === "COMPLETED" ? company.lastMeeting.summary?.reports.find(r => r.role === "portfolio-final") : undefined;
  const sameReadTime = !error && !companyError && updatedAt && companyUpdatedAt && thaiDate(updatedAt, true) === thaiDate(companyUpdatedAt, true);
  const freshness = <p className="data-freshness">{sameReadTime ? `อ่านข้อมูลล่าสุด ${thaiDate(updatedAt, true)}` : <>{error ? "ยอดเงินยังยืนยันไม่ได้" : updatedAt ? `อ่านบัญชีล่าสุด ${thaiDate(updatedAt, true)}` : "กำลังตรวจยอดเงิน"} · {companyError ? "สถานะบริษัทยังยืนยันไม่ได้" : companyUpdatedAt ? `อ่านสถานะล่าสุด ${thaiDate(companyUpdatedAt, true)}` : "กำลังตรวจสถานะ"}</>}</p>;
  if (mode === "home") return <section className="forward-home" aria-label="สถานะบริษัท">
    <div className="forward-panel-heading"><div><h2>{title}</h2><p className="company-next-step" role="status">{message}</p></div>{controls}</div>
    {!error && data && account ? <div className="forward-home-metrics"><Metric label="มูลค่าพอร์ตจำลอง" amount={data.portfolio?.equity} fallback="ยังไม่เปิดพอร์ต" /><Metric label="กำไร / ขาดทุน" amount={data.portfolio?.totalContributed ? data.portfolio.totalPnl : undefined} fallback="ยังไม่เริ่มลงทุน" /></div> : <p className="empty">{error ? "ยังยืนยันยอดเงินไม่ได้" : "รอข้อมูลบัญชี"}</p>}
    {freshness}
    {finalReport && <div className="today-result"><h3>ผลรอบล่าสุด</h3><p>{finalReport.summary}</p></div>}
    {!companyError && company?.lastMeeting && <details className="day-details"><summary>{company.lastMeeting.summary ? "รายละเอียดรอบและรายงานทีม" : "รอบล่าสุด · ไม่มีรายงานที่บันทึกไว้"}</summary><SavedCompanyDay meeting={company.lastMeeting} /></details>}
    {!companyError && company && !company.lastMeeting && <p className="empty">ยังไม่มีรอบที่บันทึกไว้สำหรับวันนี้</p>}
  </section>;
  return <section className="money-workspace" aria-label="เงินและพอร์ตจำลอง">
    <div className="money-status-bar"><p role="status">{title}</p>{controls}</div>
    {freshness}
    {error ? <p className="connection-notice" role="alert">ยังยืนยันยอดเงินไม่ได้ กำลังเชื่อมต่อใหม่ กรุณารอข้อมูลล่าสุด</p> : !data ? <p className="empty">กำลังโหลดบัญชีจำลอง…</p> : !account ? <p className="empty">ยังไม่มีบัญชีเงินเดือนจำลอง</p> : <>
      {data.status === "DISABLED" && <p>ระบบเงินเดือนยังไม่เปิด จะไม่มีการบันทึกรอบใหม่</p>}
      {data.portfolio ? <PortfolioDetails portfolio={data.portfolio} /> : <section className="money-section"><h2>พอร์ตลงทุน</h2><p>ยังไม่เปิดพอร์ตจำลอง ไม่ได้สมมติยอดเงินขึ้นมา</p></section>}
      <section className="money-section" aria-label="เงินเดือนและเงินสำรอง"><header className="section-heading"><div><h2>เงินเดือนและเงินสำรอง</h2><p>จัดสรรเงินเดือนจำลองเดือนละครั้ง · รอบถัดไป {thaiDate(account.nextPayday)}</p></div></header>
        <div className="cfo-metrics"><Metric label="เงินสำรองที่เก็บไว้" amount={account.emergencyBalance} /><Metric label="จัดสรรเพื่อลงทุนสะสม" amount={account.investmentCash} /><Metric label="รอเข้าพอร์ต" amount={data.pendingInvestment} /></div>
        <details className="transaction-details"><summary>แผนเงินเดือนและประวัติ ({account.postings.length} รอบ)</summary>
          <dl className="money-breakdown"><div><dt>เงินเดือนต่อเดือน</dt><dd>{value(account.mandate.monthlySalary)}</dd></div><div><dt>ค่าใช้จ่ายจำเป็น</dt><dd>{value(account.mandate.fixedEssentials)}</dd></div><div><dt>ค่าบ้าน / ภาระหนี้</dt><dd>{value(account.mandate.fixedDebtPayment)}</dd></div><div><dt>เงินเดือนที่บันทึกสะสม</dt><dd>{value(account.totalSalary)}</dd></div></dl>
          {account.postings.length ? <ScrollTable><table className="cfo-table"><caption>การจัดสรรเงินเดือนจำลอง</caption><thead><tr><th>รอบเงินเดือน</th><th>เงินเดือน</th><th>จำเป็น</th><th>บ้าน / หนี้</th><th>เงินสำรอง</th><th>ลงทุน</th><th>ใช้ส่วนตัว</th></tr></thead><tbody>{account.postings.map(p => <tr key={p.allocation.salaryDate}><td>{thaiDate(p.allocation.salaryDate)}<small>{thaiDate(p.postedAt, true)}</small></td>{[p.allocation.salary, p.allocation.essentials, p.allocation.debt, p.allocation.emergency, p.allocation.investment, p.allocation.discretionary].map((n, i) => <td key={i}>{value(n)}</td>)}</tr>)}</tbody></table></ScrollTable> : <p>ยังไม่มีเงินเดือนที่บันทึกไว้ รอรอบสิ้นเดือน</p>}
          <p className="simulation-truth">เงินเดือนไม่ได้ตรวจจากธนาคาร และไม่ได้นำกำไรย้อนหลังมาเติมบัญชี</p>
        </details>
      </section>
      <details className="company-detail"><summary>รหัสบัญชีจำลอง</summary><p className="identifier">{account.accountId}</p></details>
    </>}
    {companyError && <p role="alert">ยังยืนยันสถานะบริษัทไม่ได้ กำลังตรวจใหม่</p>}
    {!companyError && company && <details className="day-details"><summary>ผลรอบบริษัทและรายงานทีม</summary><SavedCompanyDay meeting={company.lastMeeting} /></details>}
    <CompanyDetails company={company} companyError={companyError} />
  </section>;
}

export function ForwardFundingPanel({ mode = "operations", onCompany }: { mode?: "home" | "operations"; onCompany?: (company: CompanyStatus) => void } = {}) {
  const [data, setData] = useState<FundingStatus>(), [error, setError] = useState("");
  const [company, setCompany] = useState<CompanyStatus>(), [companyError, setCompanyError] = useState("");
  const [updatedAt, setUpdatedAt] = useState<string>(), [companyUpdatedAt, setCompanyUpdatedAt] = useState<string>();
  const [revision, setRevision] = useState(0), [busy, setBusy] = useState(false), [controlMessage, setControlMessage] = useState("");
  const actionAbort = useRef<AbortController | null>(null);
  useEffect(() => () => actionAbort.current?.abort(), []);
  const base = process.env.NEXT_PUBLIC_API_URL || "http://localhost:8080";
  useEffect(() => {
    const controller = new AbortController(); let fundingTimer: ReturnType<typeof setTimeout>, companyTimer: ReturnType<typeof setTimeout>;
    async function refreshFunding() {
      const result = await readForwardEndpoint<FundingStatus>(`${base}/forward-funding/status`, fetch, controller.signal, validFundingStatus);
      if (controller.signal.aborted) return;
      if (result.ok) { setData(result.data); setUpdatedAt(new Date().toISOString()); setError(""); } else setError("unavailable");
      fundingTimer = setTimeout(refreshFunding, 10000);
    }
    async function refreshCompany() {
      const result = await readForwardEndpoint<CompanyStatus>(`${base}/forward-company/status`, fetch, controller.signal, validCompanyStatus);
      if (controller.signal.aborted) return;
      if (result.ok) { setCompany(result.data); setCompanyUpdatedAt(new Date().toISOString()); setCompanyError(""); onCompany?.(result.data); } else setCompanyError("unavailable");
      companyTimer = setTimeout(refreshCompany, 10000);
    }
    void refreshFunding(); void refreshCompany();
    return () => { controller.abort(); clearTimeout(fundingTimer); clearTimeout(companyTimer); };
  }, [base, revision, onCompany]);
  async function changePaused() {
    if (actionAbort.current || !company || companyError) return;
    const controller = new AbortController();
    actionAbort.current = controller;
    setBusy(true); setControlMessage("");
    const confirmed = await setSandboxPaused(base, !company.paused, fetch, controller.signal);
    actionAbort.current = null;
    if (controller.signal.aborted) return;
    setControlMessage(confirmed ? company.paused ? "บันทึกให้ทำงานต่อตามรอบแล้ว" : "บันทึกพักรอบถัดไปแล้ว" : "ยังยืนยันผลไม่ได้ ตรวจสถานะล่าสุดก่อนลองอีกครั้ง ไม่มีการส่งคำสั่งซ้ำอัตโนมัติ");
    setBusy(false); setCompanyError("unavailable"); setRevision(r => r + 1);
  }
  const controls = <div className="sandbox-controls"><button type="button" disabled={busy || !company || Boolean(companyError) || Boolean(company.error) || company.modelEnabled === false || typeof company.paused !== "boolean"} onClick={() => void changePaused()}>{busy ? "กำลังบันทึก…" : company?.paused ? "ทำงานต่อตามรอบ" : "พักรอบถัดไป"}</button><small>ควบคุม Sandbox บนเครื่องนี้</small>{controlMessage && <p className="control-feedback" role="status">{controlMessage}</p>}</div>;
  return <ForwardFundingView data={data} error={error} company={company} companyError={companyError} mode={mode} updatedAt={updatedAt} companyUpdatedAt={companyUpdatedAt} controls={controls} />;
}
