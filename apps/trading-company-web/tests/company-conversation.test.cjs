const { test } = require('node:test');
const assert = require('node:assert/strict');
const { join } = require('node:path');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const load = require('./load-component.cjs');
const contract = load(join(__dirname, '../app/companyConversation.ts'));
const { ConversationThread } = load(join(__dirname, '../app/companyConversationView.tsx'));
const { CompanyChat } = load(join(__dirname, '../app/CompanyChat.tsx'));
const message = { id: 'one', role: 'portfolio-final', createdAt: '2026-10-01T03:00:00Z', summary: '<script>bad</script>', recommendations: [], risks: ['No evidence'], proposals: [{ action: 'HOLD', symbol: 'BTC-THB', targetWeight: .2, confidence: 0, thesis: 'No edge', invalidation: 'Validated evidence' }], evidenceStatus: 'saved-report' };
const day = { businessDate: '2026-10-01', status: 'COMPLETED', updatedAt: '2026-10-01T04:00:00Z', messages: [message] };
const render = props => renderToStaticMarkup(React.createElement(ConversationThread, { selected: day.businessDate, loading: false, error: false, ...props }));

test('team chat starts closed with a labelled launcher and nonmodal persistent reader', () => {
  const html = renderToStaticMarkup(React.createElement(CompanyChat, { api: 'http://localhost:8080' }));
  assert.match(html, /aria-expanded="false"/);
  assert.match(html, /aria-controls="company-team-chat"/);
  assert.match(html, /role="dialog"/);
  assert.match(html, /hidden=""/);
  assert.doesNotMatch(html, /aria-modal="true"|<textarea/);
  assert.match(html, /กำลังอ่านรายการวันที่/);
  assert.doesNotMatch(html, /ยังไม่มีวันที่มีรายงาน/);
});
test('real uppercase proposals and valid dates are accepted; malformed values cannot throw', () => {
  assert.equal(contract.validConversationDay(day), true);
  for (const invalid of ['2026-99-99', '2026-02-30', 'wrong']) assert.equal(contract.validConversationDate(invalid), false);
  assert.equal(contract.validConversationDay({ ...day, messages: [message, message] }), false);
  assert.equal(contract.validConversationDay({ ...day, messages: [{ ...message, proposals: [{ ...message.proposals[0], targetWeight: 2 }] }] }), false);
  assert.equal(contract.validConversationDay({ ...day, messages: [{ ...message, proposals: [{ ...message.proposals[0], action: 'EXECUTED' }] }] }), false);
});
test('reports are escaped and distinguish proposals, provenance and ratio units', () => {
  const html = render({ day });
  assert.match(html, /20\.0%/); assert.match(html, /ยังไม่ยืนยันกับผลรอบ/); assert.match(html, /ไม่ใช่ผลซื้อขายจริง/);
  assert.match(html, /&lt;script&gt;/); assert.doesNotMatch(html, /<script>/);
});
test('changing days hides old reports; empty dates and unavailable database are distinct', () => {
  assert.doesNotMatch(render({ selected: '2026-10-02', day }), /bad|No edge|บันทึกว่าจบรอบ/);
  assert.match(render({ day: { ...day, messages: [] } }), /ยังไม่มีรายงานที่บันทึกไว้/);
  const unavailable = render({ day, error: true });
  assert.match(unavailable, /กำลังลองเชื่อมต่อใหม่/); assert.doesNotMatch(unavailable, /No edge/);
});
test('absence of execution is shown only after the paper ledger was verified', () => {
  assert.doesNotMatch(render({ day }), /ไม่มีคำสั่งหรือผลซื้อขายจำลอง/);
  assert.match(render({ day: { ...day, executionVerified: true } }), /ไม่มีคำสั่งหรือผลซื้อขายจำลอง/);
  assert.doesNotMatch(render({ day: { ...day, executionVerified: true, messages: [{ ...message, role: 'risk-engine' }] } }), /ไม่มีคำสั่งหรือผลซื้อขายจำลอง/);
});
