"use client";
import { useEffect, useState } from "react";
import { companyLabels, roleLabels } from "./ForwardFundingPanel";
import { readConversation, validConversationDates, validConversationDay, validConversationDate, type ConversationDay } from "./companyConversation";

const today = () => new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Bangkok" }).format(new Date());
export const conversationDate = (v: string, time = false) => new Intl.DateTimeFormat("th-TH", {
  timeZone: "Asia/Bangkok", day: "numeric", month: "short", year: "numeric", ...(time ? { hour: "2-digit", minute: "2-digit" } : {}),
}).format(new Date(v.length === 10 ? `${v}T00:00:00Z` : v));

export function CompanyConversation({ api }: { api: string }) {
  const [dates, setDates] = useState<string[]>([]), [selected, setSelected] = useState("");
  const [day, setDay] = useState<ConversationDay>(), [datesError, setDatesError] = useState(false), [dayError, setDayError] = useState(false);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    const c = new AbortController(); let active = true, timer: ReturnType<typeof setTimeout>;
    async function load() {
      if (!active) return;
      const r = await readConversation(`${api}/sandbox/conversations`, validConversationDates, fetch, c.signal);
      if (!active) return;
      setDatesError(!r.ok);
      if (r.ok) { const next = r.data.dates.slice().sort().reverse(); setDates(next); setSelected(s => s || next[0] || today()); }
      else setLoading(false);
      timer = setTimeout(load, 10000);
    }
    void load();
    return () => { active = false; c.abort(); clearTimeout(timer); };
  }, [api]);
  useEffect(() => {
    if (!validConversationDate(selected)) return;
    const c = new AbortController(); let active = true, timer: ReturnType<typeof setTimeout>;
    setDay(undefined); setDayError(false); setLoading(true);
    async function load() {
      if (!active) return;
      const r = await readConversation(`${api}/sandbox/conversations/${selected}`, validConversationDay, fetch, c.signal);
      if (!active) return;
      setLoading(false);
      const valid = r.ok && r.data.businessDate === selected;
      setDayError(!valid); setDay(valid ? r.data : undefined);
      timer = setTimeout(load, 10000);
    }
    void load();
    return () => { active = false; c.abort(); clearTimeout(timer); };
  }, [api, selected]);

  return <section className="company-conversation" aria-label="บทสนทนารายงานทีม">
    <header className="conversation-heading">
      <div><h2>บทสนทนาของทีม</h2><p>อ่านตามลำดับว่าใครเสนอ ทักท้วง และสรุปอะไร</p></div>
    </header>
    <div className="conversation-layout">
      <aside className="conversation-sidebar" aria-label="เลือกวันประชุม">
      <div className="conversation-controls">
        <label>วันที่<input type="date" value={selected || today()} max={today()} onChange={e => { if (validConversationDate(e.target.value)) setSelected(e.target.value); }} /></label>
        <button type="button" onClick={() => setSelected(today())}>วันนี้</button>
      </div>
      <h3>วันที่มีรายงาน</h3>
      <nav className="conversation-days" aria-label="ประวัติการประชุม">
        {dates.map(date => <button type="button" key={date} aria-current={selected === date ? "date" : undefined} onClick={() => setSelected(date)}>{conversationDate(date)}</button>)}
        {!dates.length && <p>{datesError ? "ยังอ่านรายการวันที่ไม่ได้" : loading ? "กำลังอ่านรายการวันที่…" : "ยังไม่มีวันที่มีรายงาน"}</p>}
      </nav>
      </aside>
      <div className="conversation-thread">
        <ConversationThread key={selected} selected={selected} day={day} loading={loading} error={datesError || dayError} />
      </div>
    </div>
  </section>;
}

export function ConversationThread({ selected, day, loading, error }: { selected: string; day?: ConversationDay; loading: boolean; error: boolean }) {
  const current = day?.businessDate === selected ? day : undefined;
  const messages = current?.messages.slice().sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt)) ?? [];
  const final = messages.find(m => m.role === "portfolio-final");
  return <>
    <p className="conversation-meta">{selected ? conversationDate(selected) : "กำลังหาวันที่มีรายงาน"}
      {current?.status && ` · ${current.status === "COMPLETED" ? "บันทึกว่าจบรอบ" : companyLabels[current.status] ?? "สถานะรอบยังไม่ยืนยัน"}`}</p>
    <p className="conversation-caption">แสดงรายงานที่บันทึกไว้ตามลำดับ ไม่ใช่แชตสด · ข้อเสนอของทีมไม่ใช่ผลซื้อขายจริง</p>
    {!error && !loading && current?.executionVerified && !messages.some(m => m.role === "risk-engine" || m.role === "paper-execution") && <p className="conversation-meta">ตรวจบันทึกแล้ว · ไม่มีคำสั่งหรือผลซื้อขายจำลองในวันที่เลือก</p>}
    {!error && !loading && final?.proposals.length ? <aside className="conversation-decision" aria-label="ข้อสรุปที่ทีมเสนอ">
      <strong>ข้อสรุปที่ทีมเสนอ</strong>
      <p>{final.proposals.map(p => `${({ HOLD: "คงสถานะ", BUY: "เสนอซื้อ", TRIM: "เสนอลดสัดส่วน", SELL: "เสนอขาย" } as Record<string, string>)[p.action]} ${p.symbol}`).join(" · ")}</p>
    </aside> : null}
    <section className="conversation-stream" tabIndex={0} role="region" aria-label="ข้อความรายงานของทีม เลื่อนเพื่ออ่านย้อนหลัง">
    {error ? <p role="alert">อ่านรายงานทีมไม่ได้ กำลังลองเชื่อมต่อใหม่ ข้อมูลที่ขาดไม่ถือว่าไม่มีงาน</p> : loading || !current ?
      <p role="status">กำลังอ่านรายงานทีม…</p> : messages.length === 0 ? <p className="empty">วันที่เลือกยังไม่มีรายงานที่บันทึกไว้</p> : <>
      <div className="conversation-messages">
        {messages.map(message => <article className="conversation-message" key={message.id}>
          <header><strong>{roleLabels[message.role] ?? ({ "risk-engine": "ตรวจความเสี่ยง · RiskEngine", "paper-execution": "ระบบคำสั่งจำลอง" } as Record<string, string>)[message.role] ?? message.role}</strong><time dateTime={message.createdAt}>{conversationDate(message.createdAt, true)}</time></header>
          <span className="conversation-badge">{message.evidenceStatus === "accepted" ? "ยืนยันกับบันทึกระบบแล้ว" : "รายงานที่บันทึกไว้ · ยังไม่ยืนยันกับผลรอบ"}</span>
          <p>{message.summary}</p>
          {(message.recommendations.length > 0 || message.risks.length > 0) && <details><summary>ข้อเสนอแนะและข้อกังวล</summary>
            {message.recommendations.length > 0 && <><h4>ข้อเสนอแนะ</h4><ul>{message.recommendations.map((s, i) => <li key={i}>{s}</li>)}</ul></>}
            {message.risks.length > 0 && <><h4>ข้อกังวล</h4><ul>{message.risks.map((s, i) => <li key={i}>{s}</li>)}</ul></>}
          </details>}
          {message.proposals.length > 0 && <details><summary>ข้อเสนอเชิงพอร์ต ({message.proposals.length})</summary>
            {message.proposals.map((p, i) => <div className="conversation-proposal" key={i}>
              <strong>{p.action} · {p.symbol}</strong><span>สัดส่วนเป้าหมาย {(p.targetWeight * 100).toFixed(1)}%</span>
              <p>{p.thesis}</p><small>เงื่อนไขทบทวน: {p.invalidation}</small>
            </div>)}
          </details>}
        </article>)}
      </div>
    </>}
    </section>
  </>;
}
