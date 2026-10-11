"use client";
import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { requestJson } from "./requestJson";
import { OwnerPairingForm } from "./OwnerPairingForm";
import { executorOrigin, fullSpotMandate, ownerMutation, ownerMutationRejection, ownerOrigin, pairOwner, readOwnerSetup, sameOwnerConsent } from "./ownerSetupClient";
import type { OwnerSetup } from "./ownerSetupClient";
const cash = (n: number) => new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" }).format(n);
export function armDisabledReason(setup: OwnerSetup | undefined, confirmLive: boolean, expired: boolean, busy = false) {
  if (!setup?.canControl) return "ต้องยืนยันสิทธิ์ควบคุมก่อน";
  if (busy) return "กำลังตรวจสอบข้อมูล";
  if (!setup.baselineEstablished) return "ต้องตรวจและบันทึกยอดเริ่มต้นก่อน";
  if (setup.needsDailyBaseline) return "ต้องตรวจยอดเริ่มวันใหม่ก่อน";
  if (!setup.reconciled) return "ต้องตรวจยอดและคำสั่งกับ Bitkub ก่อน";
  if (expired) return "คำอนุมัติวงเงินหมดอายุ ต้องบันทึกวงเงินใหม่ก่อน";
  if (!confirmLive) return "ต้องยืนยันการใช้เงินจริงก่อน";
  return "";
}
export function LiveOwnerSetup({ onChanged }: { onChanged?: () => void }) {
  const [setup, setSetup] = useState<OwnerSetup>();
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [fullCapital, setFullCapital] = useState(false);
  const [confirmLive, setConfirmLive] = useState(false);
  const [pairCode, setPairCode] = useState("");
  const [rememberBrowser, setRememberBrowser] = useState(true);
  const [rememberControl, setRememberControl] = useState(false);
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");
  const inFlight = useRef(false);
  const abort = useRef<AbortController | null>(null);
  const consent = useRef<OwnerSetup | undefined>(undefined);
  async function load(signal?: AbortSignal) {
    if (window.location.origin !== ownerOrigin) throw new Error("เปิดแอพที่ https://localhost:3000 เพื่อควบคุมบัญชี");
    const result = await requestJson(`${executorOrigin}/live/setup`, { credentials: "include", signal });
    if (result.status === 401) throw new Error("ยืนยันเจ้าของที่หน้าบัญชีก่อนตั้งค่าการเทรด");
    const next = result.status === 200 ? readOwnerSetup(result.data) : undefined;
    if (!next) throw new Error("ยังตรวจข้อมูลตั้งค่าไม่ได้ ตรวจการเชื่อมต่อแล้วลองอ่านสถานะอีกครั้ง");
    if (!signal?.aborted) {
      if (consent.current && !sameOwnerConsent(consent.current, next)) { setFullCapital(false); setConfirmLive(false); }
      consent.current = next; setSetup(next);
    }
    return next;
  }
  useEffect(() => {
    const controller = new AbortController(); abort.current = controller;
    void load(controller.signal).catch(e => { if (!controller.signal.aborted) setError(e.message); }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => { controller.abort(); abort.current?.abort(); };
  }, []);
  async function act(path: string) {
    if (inFlight.current || !setup?.canControl) return;
    inFlight.current = true; setBusy(true); setError(""); setNotice("");
    const controller = new AbortController(); abort.current?.abort(); abort.current = controller;
    try {
      const fresh = await load(controller.signal);
      if (!fresh.canControl || !sameOwnerConsent(setup, fresh)) {
        setFullCapital(false); setConfirmLive(false); throw new Error("สิทธิ์หรือวงเงินเปลี่ยน ตรวจข้อมูลใหม่ก่อนยืนยัน");
      }
      const body = path === "/live/provision" || path === "/live/mandate" ? fullSpotMandate(fresh) : path === "/live/arm" ?
        { accountId: fresh.accountId, brokerAccountReference: fresh.brokerAccountReference, mandateVersion: fresh.mandateVersion,
          snapshotVersion: fresh.snapshotVersion, confirmLive: confirmLive, clearStop: fresh.stopped } :
        { accountId: fresh.accountId, mandateVersion: fresh.mandateVersion, snapshotVersion: fresh.snapshotVersion };
      if (!body || path === "/live/arm" && (!confirmLive || !fresh.reconciled || fresh.needsDailyBaseline)) throw new Error("ต้องตรวจยอดและยืนยันการใช้เงินจริงก่อน");
      const result = await ownerMutation(window.location.origin, path, body, fetch, controller.signal);
      setFullCapital(false); setConfirmLive(false);
      if (result.status < 200 || result.status >= 300) {
        setError(result.status === 403 ? "สิทธิ์ควบคุมหมดอายุ ยืนยันสิทธิ์จากตัวเปิดแอพใหม่ได้ โดยยังดูบัญชีต่อได้" :
          result.status === 409 ? ownerMutationRejection(result.data) :
          "ยังยืนยันผลไม่ได้ กำลังอ่านสถานะใหม่ ไม่มีการส่งคำขอซ้ำอัตโนมัติ");
      } else setNotice(path === "/live/arm" ? "รับคำขอเปิดสิทธิ์แล้ว ตรวจสถานะการเทรดด้านบน" : "บันทึกขั้นตอนแล้ว การบันทึกวงเงินหรือยอดไม่เปิดการเทรด");
      await load(controller.signal); onChanged?.();
    } catch (e) { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "ยังยืนยันผลไม่ได้ ตรวจสถานะก่อนดำเนินการอีกครั้ง"); }
    finally { inFlight.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function refresh() { if (inFlight.current) return; setLoading(true); setError(""); try { await load(abort.current?.signal); } catch (e) { setError(e instanceof Error ? e.message : "อ่านสถานะไม่ได้"); } finally { setLoading(false); } }
  async function pair() {
    if (inFlight.current) return;
    inFlight.current = true; setBusy(true); setError(""); setNotice("");
    const code = pairCode; setPairCode(""); const controller = new AbortController(); abort.current?.abort(); abort.current = controller;
    try {
      const result = await pairOwner(window.location.origin, code, rememberBrowser, rememberControl, fetch, controller.signal);
      if (!result.ok) throw new Error("จับคู่ไม่สำเร็จ ตรวจรหัสและลองใหม่");
      await load(controller.signal); onChanged?.(); setNotice(rememberControl ? "จับคู่แล้ว จดจำสิทธิ์ควบคุมไว้ 30 วัน การจับคู่ไม่เปิดการเทรด" : "จับคู่แล้ว การจับคู่ไม่เปิดการเทรด");
    } catch (e) { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "จับคู่ไม่สำเร็จ"); }
    finally { inFlight.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  const disabled = busy || !setup?.canControl;
  const expired = !!setup?.mandate?.expiresAt && Date.parse(setup.mandate.expiresAt) <= Date.now();
  return <section className="owner-setup" aria-labelledby="owner-setup-title">
    <h2 id="owner-setup-title">เปิดซื้อขายอัตโนมัติ</h2>
    {loading ? <p role="status">กำลังตรวจข้อมูลตั้งค่า…</p> : !setup?.configured ? <>
      <p>ยังไม่มีการยืนยันผลซื้อ–ขาย Bitkub สำหรับเปิดระบบอัตโนมัติ</p>
      <details><summary>สิ่งที่ต้องทำครั้งแรก</summary>
        <p>เก็บยอดก่อนซื้อ ซื้อ BTC ด้วย THB จำนวนเล็กน้อย รอจับคู่ครบและเก็บผล จากนั้นขาย BTC ที่ได้ทั้งหมดแล้วเก็บผลอีกครั้ง เพื่อตรวจยอดเหรียญ เงิน และค่าธรรมเนียมให้ตรงกัน</p>
        <p>เมื่อผลผ่านการตรวจ ระบบจะแสดงวงเงินและปุ่มเปิดซื้อขายที่นี่ <Link href="/account">ดูบัญชี</Link></p>
      </details>
    </> : <>
      {!setup.canControl && <><p className="owner-setup-alert">สิทธิ์ดูบัญชียังใช้ได้ กรุณาเปิด Pair Owner Browser.cmd แล้วนำรหัสมาวางเพื่อยืนยันสิทธิ์ควบคุม</p>
        <OwnerPairingForm code={pairCode} busy={busy} rememberBrowser={rememberBrowser} rememberControl={rememberControl}
          onCodeChange={setPairCode} onRememberBrowserChange={value => { setRememberBrowser(value); if (!value) setRememberControl(false); }}
          onRememberControlChange={setRememberControl} onPair={() => void pair()} idPrefix="setup-owner" /></>}
      {!setup.provisioned ? <form onSubmit={e => { e.preventDefault(); if (fullCapital && fullSpotMandate(setup)) void act("/live/provision"); }}>
        <p>ทุนจากบัญชีที่ตรวจแล้ว <strong>{cash(setup.availableCapital ?? 0)}</strong></p>
        <p>CEO เลือกเหรียญและขนาดคำสั่งได้ภายในทุนนี้ ซื้อขายเฉพาะ Spot เงินที่ใช้และค่าธรรมเนียมต้องไม่เกินยอดจริง</p>
        <label className="owner-setup-confirm"><input type="checkbox" checked={fullCapital} onChange={e => setFullCapital(e.target.checked)} disabled={disabled} />
          <span>อนุญาตใช้ทุนทั้งหมด และรับขีดจำกัดขาดทุนสูงสุดเท่าทุนที่อนุญาต</span></label>
        <p className="owner-setup-note">รวมกำไรที่ตรวจยอดแล้ว หมุนทุนซื้อ–ขายซ้ำได้ รับความเสี่ยงสูญเสียทุนทั้งหมด การบันทึกนี้ยังไม่เปิดการเทรด</p>
        <button type="submit" disabled={disabled || !fullCapital || !fullSpotMandate(setup)}>{busy ? "กำลังบันทึก…" : "บันทึกวงเงินสำหรับ CEO"}</button>
        {(setup.availableCapital ?? 0) <= 0 && <p>ต้องมี THB ในบัญชีที่ตรวจแล้วก่อนตั้งวงเงินจริง</p>}
      </form> : <>
        {setup.mandate?.useFullPortfolio ? <div className="owner-setup-note"><strong>ใช้ 100% ของพอร์ต</strong><p>มูลค่าพอร์ตล่าสุด {cash(setup.availableCapital ?? 0)} รวมกำไรที่ตรวจยอดแล้ว หมุนทุนซ้ำได้ โดยหักค่าธรรมเนียมและเงินกันไว้</p></div> : <><p>วงเงินเดิมที่บันทึกไว้</p><dl className="money-breakdown"><div><dt>ทุนที่อนุญาต</dt><dd>{cash(setup.mandate!.capitalAllocation)}</dd></div><div><dt>สูงสุดต่อคำสั่ง</dt><dd>{cash(setup.mandate!.maximumOrderNotional)}</dd></div><div><dt>ขาดทุนสูงสุดต่อวัน</dt><dd>{cash(setup.mandate!.maximumDailyLoss)}</dd></div></dl></>}
        <div className="owner-setup-actions">
          {!setup.baselineEstablished && <button disabled={disabled} onClick={() => void act("/live/baseline")}>ตรวจและบันทึกยอดเริ่มต้น</button>}
          {setup.needsDailyBaseline && <button disabled={disabled || setup.armed} onClick={() => void act("/live/day-baseline")}>ตรวจยอดเริ่มวันใหม่</button>}
          <button disabled={disabled} onClick={() => void act("/live/reconcile")}>ตรวจยอดและคำสั่งกับ Bitkub</button>
        </div>
        {!setup.armed && <>
          <label className="owner-setup-confirm"><input type="checkbox" checked={confirmLive} onChange={e => setConfirmLive(e.target.checked)} disabled={disabled} />
            <span>{setup.mandate?.useFullPortfolio ? "อนุญาตให้ CEO ส่งคำสั่งเงินจริงภายในนโยบายใช้ 100% ของพอร์ต" : "อนุญาตให้ CEO ส่งคำสั่งเงินจริงตามวงเงินที่บันทึก"}{setup.stopped ? " และยกเลิกคำสั่งหยุดเดิม" : ""}</span></label>
          {!setup.mandate?.useFullPortfolio && <><p className="owner-setup-note">เปลี่ยนเป็น 100% รวมกำไรที่ตรวจยอดแล้ว หมุนทุนซื้อ–ขายซ้ำได้ และรับความเสี่ยงสูญเสียทุนทั้งหมด</p><button type="button" disabled={disabled || !fullSpotMandate(setup)} onClick={() => void act("/live/mandate")}>ใช้ 100% ของพอร์ต</button></>}
          <p id="arm-disabled-reason" role="status">{armDisabledReason(setup, confirmLive, expired, busy)}</p>
          <button aria-describedby="arm-disabled-reason" disabled={disabled || !confirmLive || !setup.reconciled || !setup.baselineEstablished || setup.needsDailyBaseline || expired}
            onClick={() => void act("/live/arm")}>{busy ? "กำลังตรวจและเปิดสิทธิ์…" : "เปิดสิทธิ์ซื้อขายเงินจริง"}</button>
        </>}
      </>}
    </>}
    {error && <p className="owner-setup-alert" role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    <button type="button" disabled={busy || loading} onClick={() => void refresh()}>อ่านสถานะตั้งค่าอีกครั้ง</button>
  </section>;
}
