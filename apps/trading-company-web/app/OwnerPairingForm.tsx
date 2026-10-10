"use client";
export function OwnerPairingForm({ code, busy, rememberBrowser, rememberControl, onCodeChange,
  onRememberBrowserChange, onRememberControlChange, onPair, idPrefix = "owner" }: {
  code: string; busy: boolean; rememberBrowser: boolean; rememberControl: boolean;
  onCodeChange: (value: string) => void; onRememberBrowserChange: (value: boolean) => void;
  onRememberControlChange: (value: boolean) => void; onPair: () => void; idPrefix?: string;
}) {
  return <form className="live-owner-form" onSubmit={event => { event.preventDefault(); onPair(); }}>
    <label htmlFor={`${idPrefix}-code`}>รหัสจับคู่เจ้าของบัญชี</label>
    <input id={`${idPrefix}-code`} type="password" autoComplete="off" value={code}
      onChange={event => onCodeChange(event.target.value)} required minLength={64} maxLength={64}
      pattern="[A-Fa-f0-9]{64}" disabled={busy} aria-describedby={`${idPrefix}-code-help`} />
    <p id={`${idPrefix}-code-help`} className="live-form-help">เปิด Pair Owner Browser.cmd แล้ววางรหัสจากหน้าต่างที่เปิด ภายใน 5 นาที</p>
    <label className="live-remember-browser owner-setup-confirm"><input type="checkbox" checked={rememberBrowser}
      onChange={event => { onRememberBrowserChange(event.target.checked); if (!event.target.checked) onRememberControlChange(false); }} disabled={busy} />จำเบราว์เซอร์นี้สำหรับดูบัญชี 30 วัน</label>
    <label className="live-remember-browser owner-setup-confirm"><input type="checkbox" checked={rememberBrowser && rememberControl}
      onChange={event => onRememberControlChange(event.target.checked)} disabled={busy || !rememberBrowser} />จำสิทธิ์ควบคุมบนเครื่องนี้ 30 วัน</label>
    <p className="live-form-help">การจับคู่ไม่เปิดการเทรด ออกจากระบบเพื่อลืมสิทธิ์ของเครื่องนี้</p>
    <div className="live-owner-form-actions"><button disabled={busy || !/^[A-Fa-f0-9]{64}$/.test(code)} type="submit">{busy ? "กำลังยืนยัน…" : "ยืนยันเจ้าของ"}</button></div>
  </form>;
}
