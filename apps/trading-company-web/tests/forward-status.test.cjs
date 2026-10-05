const { test } = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const load = require('./load-component.cjs');
const { validFundingStatus, validCompanyStatus } = load(path.join(__dirname, '../app/forwardStatus.ts'));
const { requestJson } = load(path.join(__dirname, '../app/requestJson.ts'));
const { readForwardEndpoint, setSandboxPaused } = load(path.join(__dirname, '../app/ForwardFundingPanel.tsx'));
const account = { accountId: 'fixture', nextPayday: '2026-10-31', totalSalary: 0, emergencyBalance: 0, investmentCash: 0, postings: [], mandate: { monthlySalary: 70000, fixedEssentials: 24400, fixedDebtPayment: 14000 } };

test('valid JSON with missing financial structure is rejected before rendering', async () => {
  const bad = { status: 'PAPER_LEDGER_READY', account: { postings: [] } };
  assert.equal(validFundingStatus(bad), false);
  assert.deepEqual(await readForwardEndpoint('/fixture', async () => Response.json(bad)), { ok: false, error: 'invalid response' });
});
test('legacy and current account shapes remain readable without assumed optional values', () => {
  for (const portfolio of [null, { cash: 8295, totalContributed: 8295, equity: 8295, totalPnl: 0, currency: 'THB', tradingStatus: 'TRADING_NOT_ENABLED', credits: [{ salaryDate: '2026-09-30', amount: 8295, creditedAt: '2026-09-30T12:00:00Z' }] }])
    assert.equal(validFundingStatus({ status: 'PORTFOLIO_FUNDED', account, portfolio }), true);
  assert.equal(validFundingStatus({ status: 'DISABLED', account: null, portfolio: null }), true);
});
test('non-finite financial values, invalid arrays and unknown order sides are rejected', () => {
  assert.equal(validFundingStatus({ status: 'X', account: { ...account, totalSalary: NaN } }), false);
  assert.equal(validFundingStatus({ status: 'X', account: { ...account, postings: {} } }), false);
  assert.equal(validFundingStatus({ status: 'X', account: { ...account, nextPayday: '2026-02-31' } }), false);
  assert.equal(validFundingStatus({ status: 'X', account, portfolio: { cash: 1, totalContributed: 1, equity: 1, totalPnl: 0, currency: 'THB', tradingStatus: 'X', pendingOrders: [{ id: 'x', symbol: 'BTC-THB', side: 5, quantity: 1, decisionAt: '2026-10-01T02:00:00Z' }] } }), false);
});
test('company reports require actual usage, reports and date shapes', () => {
  assert.equal(validCompanyStatus({ status: 'COMPLETED', modelEnabled: true, lastMeeting: { businessDate: '2026-10-01', status: 'COMPLETED' } }), true);
  assert.equal(validCompanyStatus({ status: 'COMPLETED', modelEnabled: true, lastMeeting: { businessDate: '2026-10-01', status: 'COMPLETED', summary: { modelCalls: 8, inputTokens: 1, outputTokens: 2, reports: [] } } }), true);
  assert.equal(validCompanyStatus({ status: 'COMPLETED', modelEnabled: true, lastMeeting: { businessDate: 'bad', status: 'COMPLETED' } }), false);
  assert.equal(validCompanyStatus({ status: 'COMPLETED', modelEnabled: true, lastMeeting: { businessDate: '2026-10-01', status: 'COMPLETED', summary: { reports: [] } } }), false);
});
test('a hung fetch expires deterministically and aborts transport', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  let signal;
  const pending = requestJson('/hung', {}, async (_, options) => { signal = options.signal; return new Promise(() => {}); });
  const rejected = assert.rejects(pending, /timeout/);
  t.mock.timers.tick(8000);
  await rejected;
  assert.equal(signal.aborted, true);
});
test('deadline includes a hung JSON body, not just HTTP headers', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const pending = requestJson('/hung-body', {}, async () => ({ ok: true, status: 200, json: () => new Promise(() => {}) }));
  const rejected = assert.rejects(pending, /timeout/);
  await Promise.resolve();
  t.mock.timers.tick(8000);
  await rejected;
});
test('unmount abort does not wait for a fetcher that ignores its signal', async () => {
  const controller = new AbortController();
  const pending = requestJson('/hung', { signal: controller.signal }, async () => new Promise(() => {}));
  const rejected = assert.rejects(pending, /aborted/);
  controller.abort();
  await rejected;
});
test('successful empty mutation response is accepted without parsing JSON or retrying', async () => {
  let calls = 0;
  assert.deepEqual(await requestJson('/action', { method: 'POST' }, async () => { calls++; return new Response(null, { status: 204 }); }), { status: 204, data: null });
  assert.equal(calls, 1);
});
test('Sandbox mutation requires a capability and matching authoritative confirmation, never retries', async () => {
  let calls = 0;
  const fetcher = async (_, options) => {
    calls++;
    if (options.method !== 'POST') return Response.json({ token: 'A'.repeat(64) });
    assert.deepEqual(JSON.parse(options.body), { paused: true });
    assert.equal(options.headers['X-Sandbox-Control'], 'A'.repeat(64));
    return Response.json({ paused: false });
  };
  assert.equal(await setSandboxPaused('http://fixture', true, fetcher), false);
  assert.equal(calls, 2);
  calls = 0;
  assert.equal(await setSandboxPaused('http://fixture', true, async () => { calls++; return Response.json({ token: 'invalid' }); }), false);
  assert.equal(calls, 1);
});
