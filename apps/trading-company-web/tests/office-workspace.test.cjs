const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const load = require('./load-component.cjs');
const { LiveOfficeWorkspaceView, LiveTeamSummaryView } = load(path.join(__dirname, '../app/LiveAccountPanel.tsx'));
const { CompanyChat } = load(path.join(__dirname, '../app/CompanyChat.tsx'));
const render = (Component, props) => renderToStaticMarkup(React.createElement(Component, props));
const connected = (state, reasons = []) => ({ kind: 'connected', status: { state, reasons, armed: state === 'ARMED' } });
const wallet = amount => ({ source: 'Bitkub', observedAt: '2026-10-06T05:00:00Z', balances: [{ currency: 'THB', available: amount, reserved: 0, total: amount }] });

test('office uses one compact state line and keeps the frame accessible without a repeated heading', () => {
  for (const [state, label] of [
    [{ kind: 'loading' }, 'กำลังตรวจสถานะ Live'],
    [{ kind: 'pair-required' }, 'เชื่อมบัญชีเพื่อดูสถานะทีม'],
    [{ kind: 'unavailable' }, 'ยังยืนยันสถานะ Live ไม่ได้'],
    [connected('DISABLED'), 'Live ยังไม่เปิดใช้งาน'],
    [connected('ARMED'), 'ระบบรายงานว่าเปิดสิทธิ์ส่งคำสั่ง'],
    [connected('STOPPED'), 'เจ้าของสั่งหยุดแล้ว'],
  ]) {
    const html = render(LiveOfficeWorkspaceView, { state });
    assert.ok(html.includes(`<p class="office-status" role="status">${label}</p>`));
    assert.equal((html.match(new RegExp(label, 'g')) || []).length, 1);
    assert.match(html, /<section class="office-summary" aria-label="สถานะการเทรด Live">/);
    assert.match(html, /<section class="live-office" aria-label="สำนักงาน Pixel ของ Quantara">/);
    assert.match(html, /<iframe[^>]*title="สำนักงาน Pixel ของ Quantara"[^>]*src="\/office\/live.html"/);
    assert.doesNotMatch(html, /<h[1-6][^>]*>สำนักงาน Pixel|office-status-heading|workspace-primary/);
    assert.equal((html.match(/<h2/g) || []).length, 1);
    if (state.kind === 'loading' || state.kind === 'pair-required' || state.status?.state === 'DISABLED') {
      assert.doesNotMatch(html, /office-status-details/);
    }
  }
});

test('read-only cash does not imply an active team or trading permission', () => {
  const html = render(LiveOfficeWorkspaceView, { state: connected('DISABLED', ['EXECUTOR_NOT_CONFIGURED']), wallet: wallet(3210) });
  assert.match(html, /Live ยังไม่เปิดใช้งาน/);
  assert.doesNotMatch(html, /ระบบส่งคำสั่งยังไม่เปิด/);
  assert.match(html, /฿3,210\.00/);
  assert.match(html, /<time dateTime="2026-10-06T05:00:00Z">/);
  assert.match(html, /href="\/trading"[^>]*>ตรวจความพร้อมเพื่อเปิด Live/);
  assert.doesNotMatch(html, /ก่อนทีมเริ่มซื้อขาย|data-complete=/);
  assert.doesNotMatch(html, /ภาพ Pixel|การอ่านยอดไม่เปิดสิทธิ์ซื้อขาย|เปิดห้องทีม/);
  assert.doesNotMatch(html, /กำลังประชุม|กำลังวิเคราะห์|ทีมกำลังทำงาน/);
});

test('missing account evidence is unknown, while a verified zero balance remains zero', () => {
  const unknown = render(LiveOfficeWorkspaceView, { state: { kind: 'pair-required' } });
  assert.match(unknown, /href="\/account"[^>]*>เชื่อมบัญชี/);
  assert.match(unknown, /ยังยืนยันยอดไม่ได้/);
  assert.doesNotMatch(unknown, /฿0\.00|data-complete="true"/);
  const empty = render(LiveOfficeWorkspaceView, { state: connected('DISABLED'), wallet: wallet(0) });
  assert.match(empty, /฿0\.00/);
  assert.doesNotMatch(empty, /ก่อนทีมเริ่มซื้อขาย|data-complete=/);
});

test('reconciliation and permission are separate from actual team activity', () => {
  const account = { snapshot: { cashBalance: 4000, cashReserved: 200, reconciled: true, observedAt: '2026-10-06T05:00:00Z' } };
  const armed = render(LiveOfficeWorkspaceView, { state: connected('ARMED'), account });
  assert.match(armed, /฿3,800\.00/);
  assert.match(armed, /ทุกคำสั่งยังต้องผ่านการตรวจความเสี่ยง/);
  assert.match(armed, /<details class="office-status-details"><summary>รายละเอียดสถานะ<\/summary>/);
  assert.doesNotMatch(armed, /กำลังประชุม|กำลังวิเคราะห์|ทีมกำลังทำงาน/);
  const stopped = render(LiveOfficeWorkspaceView, { state: connected('STOPPED'), account });
  assert.match(stopped, /สินทรัพย์และคำสั่งค้างยังต้องตรวจ/);
  assert.match(stopped, /<details class="office-status-details">/);
  const lost = render(LiveOfficeWorkspaceView, { state: { kind: 'unavailable' } });
  assert.match(lost, /ไม่ได้ยืนยันว่าระบบหยุดเทรดแล้ว/);
  assert.match(lost, /<details class="office-status-details">/);
  assert.doesNotMatch(stopped + lost, /<details[^>]*\bopen/);
});

test('headingless team label distinguishes configured staff from actual work', () => {
  const activity = { accountContext: { mode: 1, accountId: '11223344556677889900aabbccddeeff' }, meetingId: '11223344-5566-7788-9900-aabbccddeeff', observedAt: '2026-10-06T05:00:00Z',
    roles: [{ role: 'market-research', status: 'working', changedAt: '2026-10-06T05:00:00Z' }] };
  const working = render(LiveOfficeWorkspaceView, { state: connected('ARMED'), activity });
  assert.match(working, /<p class="live-office-activity" role="status">ทีม 8 บทบาท · กำลังทำงาน 1 · ทีมตลาด กำลังวิเคราะห์<\/p>/);
  assert.doesNotMatch(working, /<h[1-6][^>]*>ทีมตลาด|<h[1-6][^>]*>สำนักงาน Pixel/);
  const idle = render(LiveOfficeWorkspaceView, { state: connected('ARMED'), activity: { ...activity, roles: [{ ...activity.roles[0], status: 'completed' }] } });
  assert.match(idle, /ทีม 8 บทบาท/);
  assert.doesNotMatch(idle, /กำลังทำงาน|กำลังวิเคราะห์/);
});

test('idle chat gives one status, a recovery route, and avoids fabricated meeting absence', () => {
  const html = render(LiveTeamSummaryView, { state: connected('DISABLED'), wallet: wallet(3210) });
  assert.equal((html.match(/Live ยังไม่เปิดใช้งาน/g) || []).length, 1);
  assert.match(html, /href="\/trading"/);
  assert.doesNotMatch(html, /ยังไม่มีรายงาน|ยังไม่มีรอบ/);
});

test('compact chat preserves the single minimize control and the standard reports window', () => {
  for (const compact of [false, true]) {
    const html = render(CompanyChat, { compact, children: React.createElement('p', null, 'status') });
    assert.match(html, new RegExp(`data-compact="${compact}"`));
    assert.equal((html.match(/aria-label="ย่อแชตทีม"/g) || []).length, 1);
    assert.doesNotMatch(html, /ปิดแชตทีม/);
  }
});
