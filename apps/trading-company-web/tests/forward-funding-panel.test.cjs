const assert = require('node:assert/strict');
const { test } = require('node:test');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const component = { exports: require('./load-component.cjs')(join(__dirname, '../app/ForwardFundingPanel.tsx')) };
const render = props => renderToStaticMarkup(React.createElement(component.exports.ForwardFundingView, props));
const account = { accountId: 'account-one', nextPayday: '2026-09-30', totalSalary: 70000, investmentCash: 8295, emergencyBalance: 14000, postings: [], mandate: { monthlySalary: 70000, fixedEssentials: 24400, fixedDebtPayment: 14000 } };

test('waiting for the daily schedule explains inactivity without claiming work', () => {
  const html = render({ mode: 'home', data: { status: 'PORTFOLIO_FUNDED', account, portfolio: { totalContributed: 100000 } }, company: { status: 'WAITING_FOR_WORKDAY', modelEnabled: true } });
  assert.match(html, /เริ่มอัตโนมัติหลัง 09:00 น\. เวลาไทย วันละหนึ่งรอบ/);
  assert.doesNotMatch(html, /ทีมกำลังศึกษาตลาด|ทีมกำลังประชุม|จบรอบวิเคราะห์แล้ว/);
});

test('saved daily reports survive a fresh render without implying a fill', () => {
  const html = render({ mode: 'home', data: { status: 'PORTFOLIO_FUNDED', account }, company: { modelEnabled: true, status: 'COMPLETED', lastMeeting: { businessDate: '2026-10-01', status: 'COMPLETED', summary: { modelCalls: 8, inputTokens: 800, outputTokens: 400, reports: [{ role: 'portfolio-final', summary: 'Remain in cash because evidence is weak.', providerModel: 'test-only', artifactId: 'report-1', dataCutoff: '2026-09-30' }] } } } });
  assert.match(html, /Remain in cash because evidence is weak/);
  assert.match(html, /1 ต\.ค\. 2569/);
  assert.match(html, /1?200 tokens/);
  assert.match(html, /การจบประชุมไม่ได้หมายความว่าซื้อขายสำเร็จ/);
});

test('failed meetings show usage but suppress unvalidated reports', () => {
  const html = render({ company: { status: 'INVALID_FINAL', lastMeeting: { businessDate: '2026-10-01', status: 'INVALID_FINAL', summary: { modelCalls: 8, inputTokens: 800, outputTokens: 400, reports: [{ role: 'portfolio-final', summary: 'untrusted proposal', artifactId: 'unsafe' }] } } } });
  assert.match(html, /INVALID_FINAL/);
  assert.match(html, /1200 tokens/);
  assert.doesNotMatch(html, /untrusted proposal/);
});

test('old journals and unavailable company status never fabricate daily evidence', () => {
  const old = render({ company: { lastMeeting: { businessDate: '2026-10-01', status: 'COMPLETED' } } });
  assert.match(old, /ไม่สามารถสร้างข้อมูลย้อนหลังได้/);
  const unavailable = render({ companyError: 'offline', company: { lastMeeting: { businessDate: '2026-10-01', status: 'COMPLETED', summary: { reports: [{ summary: 'stale report' }] } } } });
  assert.doesNotMatch(unavailable, /stale report/);
});

test('public crypto data is distinct from unavailable optional equity credentials', () => {
  const html = render({ mode: 'operations', data: { status: 'PAPER_LEDGER_READY', account }, company: { modelEnabled: true, publicCryptoQuotes: true, equityQuotesConfigured: false, supportedSymbols: ['BTC-THB', 'ETH-THB', 'SOL-THB'] } });
  assert.match(html, /Bitkub/);
  assert.match(html, /0\.25%/);
  assert.match(html, /หุ้นยังไม่เปิด/);
  assert.doesNotMatch(html, /ยังไม่ได้ตั้งค่าข้อมูลราคาสำหรับบันทึกคำสั่ง/);
});

test('v2 separates cash, equity, contributions and PnL and renders ledger details', () => {
  const html = render({ data: { status: 'PAPER_LEDGER_READY', account, portfolio: { cash: 1000, totalContributed: 1400, totalPnl: 500, equity: 1900, realizedPnl: 400, drawdown: 50, currency: 'THB', tradingStatus: 'PAPER_LEDGER_READY', positions: [{ symbol: { value: 'AAPL' }, quantity: 2, averagePrice: 400, marketPrice: 450, marketValue: 900, unrealizedPnl: 100 }], pendingOrders: [{ id: 'order-test-id', symbol: 'AAPL', side: 0, quantity: 1, decisionAt: '2026-01-02T10:00:00Z' }], receipts: [{ orderId: 'reject-1', quote: { symbol: 'AAPL', nativePrice: 450, currency: 'USD', thbPerUnit: 35, observedAt: '2026-01-02T10:00:00Z', availableAt: '2026-01-02T10:00:00Z', source: 'fixture', feeThb: 1, slippageThbPerUnit: 2 }, status: 'RISK_REJECTED', violations: ['MAX_POSITION'] }] } } });
  assert.match(html, /มูลค่าพอร์ต.*฿1,900\.00/); assert.match(html, /เงินสดในพอร์ต.*฿1,000\.00/); assert.match(html, /เงินลงทุนสะสม.*฿1,400\.00/); assert.match(html, /ไม่ผ่านการตรวจความเสี่ยง/); assert.match(html, /MAX_POSITION/); assert.match(html, /คำสั่งที่รอดำเนินการ/); assert.match(html, /US\$450\.00/);
});

test('missing portfolio and errors do not invent zeros; company failure is separate', () => {
  const html = render({ data: { status: 'FUNDING_ONLY', account, portfolio: null }, companyError: 'timeout' });
  assert.match(html, /ยังไม่เปิดพอร์ตจำลอง/); assert.doesNotMatch(html, /เงินสดในพอร์ต.*฿0/); assert.match(html, /ยังยืนยันสถานะบริษัทไม่ได้/);
  assert.match(render({ error: 'Account unavailable', company: { status: 'DISABLED', modelEnabled: false } }), /ยังยืนยันยอดเงินไม่ได้/);
});

test('disabled model and missing company state are explicit', () => {
  assert.match(render({ data: { status: 'PAPER_LEDGER_READY', account, portfolio: null }, company: { status: 'MODEL_DISABLED', modelEnabled: false } }), /AI: ยังไม่เปิด/);
  assert.match(render({ data: { status: 'PAPER_LEDGER_READY', account, portfolio: null }, company: { status: 'PAPER_LEDGER_READY' } }), /AI: ยังยืนยันไม่ได้/);
});

test('unknown receipt status is not treated as filled', () => {
  const html = render({ data: { status: 'PAPER_LEDGER_READY', account, portfolio: { cash: 1, totalContributed: 1, totalPnl: 0, equity: 1, currency: 'THB', tradingStatus: 'PAPER_LEDGER_READY', receipts: [{ orderId: 'x', quote: { symbol: 'AAPL', nativePrice: 1, currency: 'USD', thbPerUnit: 35, source: 'fixture' }, status: 'BROKEN', violations: [] }] } } });
  assert.match(html, /ยังยืนยันผลไม่ได้/); assert.doesNotMatch(html, /บันทึกซื้อขายจำลองแล้ว/);
});

test('malformed endpoint JSON is contained and does not reject the reader', async () => {
  const reader = component.exports.readForwardEndpoint;
  const result = await reader('/bad', async () => ({ ok: true, json: async () => { throw new Error('malformed'); } }));
  assert.deepEqual(result, { ok: false, error: 'unavailable' });
});

test('v1 portfolio remains compatible', () => {
  const html = render({ data: { status: 'PORTFOLIO_FUNDED', account, portfolio: { cash: 8295, totalContributed: 8295, totalPnl: 0, equity: 8295, currency: 'THB', tradingStatus: 'TRADING_NOT_ENABLED', credits: [{ salaryDate: '2026-09-30', amount: 8295, creditedAt: '2026-09-30T12:00:00Z' }] } } });
  assert.match(html, /รายการเงินเข้าพอร์ตจากเงินเดือนจำลอง/); assert.match(html, /บัญชีจำลอง/);
});

test('home does not suggest normal trading work when the AI company is disabled', () => {
  const html = render({ mode: 'home', data: { status: 'PAPER_LEDGER_READY', account, portfolio: { cash: 8295, totalContributed: 8295, totalPnl: 0, equity: 8295, currency: 'THB', tradingStatus: 'TRADING_NOT_ENABLED' } }, company: { status: 'MODEL_DISABLED', modelEnabled: false } });
  assert.match(html, /AI ยังไม่เปิดทำงาน/);
  assert.doesNotMatch(html, /ดูผลตัดสินใจและรายการ/);
});

test('home does not present stale enabled AI state as verified during company outage', () => {
  const html = render({ mode: 'home', companyError: 'offline', company: { status: 'COMPLETED', modelEnabled: true, model: 'stale-model' } });
  assert.match(html, /ยังยืนยันไม่ได้/);
  assert.doesNotMatch(html, /เปิดใช้งาน \(stale-model\)/);
});

test('home distinguishes zero contributed capital from a zero trading return', () => {
  const props = { data: { status: 'AWAITING_INVESTMENT_BUDGET', account, portfolio: { equity: 0, totalContributed: 0, totalPnl: 0 } }, company: { status: 'AWAITING_INVESTMENT_BUDGET', modelEnabled: true, quoteCredentialsConfigured: false } };
  const html = render({ ...props, mode: 'home' });
  assert.match(html, /ยังไม่เริ่มลงทุน/);
  assert.match(render({ ...props, mode: 'operations' }), /ยังไม่ได้ตั้งค่าข้อมูลราคา/);
});
