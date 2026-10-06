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

test('read-only cash does not imply an active team or trading permission', () => {
  const html = render(LiveOfficeWorkspaceView, { state: connected('DISABLED', ['EXECUTOR_NOT_CONFIGURED']), wallet: wallet(3210) });
  assert.match(html, /Live ยังไม่เปิดใช้งาน/);
  assert.match(html, /ระบบส่งคำสั่งยังไม่เปิด/);
  assert.match(html, /฿3,210\.00/);
  assert.match(html, /href="\/trading"[^>]*>ตรวจความพร้อมเพื่อเปิด Live/);
  assert.equal((html.match(/data-complete="true"/g) || []).length, 1);
  assert.match(html, /ภาพ Pixel ยังไม่เชื่อมกิจกรรมสด/);
  assert.doesNotMatch(html, /กำลังประชุม|กำลังวิเคราะห์|ทีมกำลังทำงาน/);
});

test('missing account evidence is unknown, while a verified zero balance remains zero', () => {
  const unknown = render(LiveOfficeWorkspaceView, { state: { kind: 'pair-required' } });
  assert.match(unknown, /href="\/account"[^>]*>เชื่อมบัญชี/);
  assert.match(unknown, /ยังยืนยันยอดไม่ได้/);
  assert.doesNotMatch(unknown, /฿0\.00|data-complete="true"/);
  const empty = render(LiveOfficeWorkspaceView, { state: connected('DISABLED'), wallet: wallet(0) });
  assert.match(empty, /฿0\.00/);
  assert.equal((empty.match(/data-complete="true"/g) || []).length, 1);
});

test('reconciliation and permission are separate from actual team activity', () => {
  const account = { snapshot: { cashBalance: 4000, cashReserved: 200, reconciled: true, observedAt: '2026-10-06T05:00:00Z' } };
  const armed = render(LiveOfficeWorkspaceView, { state: connected('ARMED'), account });
  assert.equal((armed.match(/data-complete="true"/g) || []).length, 3);
  assert.match(armed, /฿3,800\.00/);
  assert.match(armed, /ทุกคำสั่งยังต้องผ่านการตรวจความเสี่ยง/);
  assert.doesNotMatch(armed, /กำลังประชุม|กำลังวิเคราะห์|ทีมกำลังทำงาน/);
  const stopped = render(LiveOfficeWorkspaceView, { state: connected('STOPPED'), account });
  assert.equal((stopped.match(/data-complete="true"/g) || []).length, 2);
  assert.match(stopped, /สินทรัพย์และคำสั่งค้างยังต้องตรวจ/);
  const lost = render(LiveOfficeWorkspaceView, { state: { kind: 'unavailable' } });
  assert.match(lost, /ไม่ได้ยืนยันว่าระบบหยุดเทรดแล้ว/);
});

test('idle chat gives one status, a recovery route, and avoids fabricated meeting absence', () => {
  const html = render(LiveTeamSummaryView, { state: connected('DISABLED'), wallet: wallet(3210) });
  assert.equal((html.match(/Live ยังไม่เปิดใช้งาน/g) || []).length, 1);
  assert.match(html, /ยังอ่านรายงาน Live ที่ผูกกับบัญชีไม่ได้/);
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
