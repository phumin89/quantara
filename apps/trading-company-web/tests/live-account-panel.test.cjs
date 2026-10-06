const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const loaded = { exports: require('./load-component.cjs')(path.join(__dirname, '../app/LiveAccountPanel.tsx')) };
const { readLiveStatus, readLiveObservation, postOwnerAction, LiveReadinessView } = loaded.exports;
const render = props => renderToStaticMarkup(React.createElement(LiveReadinessView, props));

test('HTTP sandbox origin never contacts owner executor', async () => {
  let calls = 0;
  const result = await readLiveStatus('http://localhost:3000', async () => { calls++; });
  assert.equal(result.kind, 'https-required');
  assert.equal(calls, 0);
});
test('status reads use only fixed executor, credentials and no cache', async () => {
  const result = await readLiveStatus('https://localhost:3000', async (url, options) => {
    assert.equal(url, 'https://localhost:7443/live/status');
    assert.equal(options.credentials, 'include');
    assert.equal(options.cache, 'no-store');
    return Response.json({ state: 'DISABLED', armed: false, reasons: ['EXECUTOR_NOT_CONFIGURED'] });
  });
  assert.equal(result.kind, 'connected');
  assert.equal(result.status.state, 'DISABLED');
});
test('expired owner session is not a zero balance or connected state', async () => {
  const result = await readLiveStatus('https://localhost:3000', async () => new Response(null, { status: 401 }));
  assert.deepEqual(result, { kind: 'pair-required' });
});
test('malformed or inconsistent state never reports ready', async () => {
  for (const body of [{}, { state: 'ARMED', armed: false, reasons: [] }, { state: 'ARMED', armed: true, reasons: ['STALE_SNAPSHOT'] }]) {
    const result = await readLiveStatus('https://localhost:3000', async () => Response.json(body));
    assert.equal(result.kind, 'unavailable');
  }
});
test('network errors are sanitized', async () => {
  const result = await readLiveStatus('https://localhost:3000', async () => { throw Error('private-details'); });
  assert.deepEqual(result, { kind: 'unavailable' });
});
test('owner action gets fresh CSRF token and never retries', async () => {
  const calls = [];
  const result = await postOwnerAction('https://localhost:3000', 'stop', undefined, async (url, options) => {
    calls.push(url);
    if (url.endsWith('/owner/csrf')) return Response.json({ token: 'one-use-test' });
    assert.equal(options.headers['X-CSRF-TOKEN'], 'one-use-test');
    assert.equal(options.credentials, 'include');
    assert.equal(options.method, 'POST');
    return new Response(null, { status: 503 });
  });
  assert.equal(result.ok, false);
  assert.equal(calls.length, 2);
});
test('pair serializes browser remembering consent while other actions have no body', async () => {
  const bodies = [];
  await postOwnerAction('https://localhost:3000', 'pair', 'pair-code', async (url, options) => {
    if (url.endsWith('/owner/csrf')) return Response.json({ token: 'token' });
    bodies.push(options.body); return new Response(null, { status: 204 });
  }, undefined, true);
  await postOwnerAction('https://localhost:3000', 'logout', undefined, async (url, options) => {
    if (url.endsWith('/owner/csrf')) return Response.json({ token: 'token' });
    bodies.push(options.body); return new Response(null, { status: 204 });
  }, undefined, true);
  assert.deepEqual(bodies, [JSON.stringify({ code: 'pair-code', rememberBrowser: true }), undefined]);
});
test('no mutation is sent without HTTPS or a CSRF token', async () => {
  let calls = 0;
  assert.equal((await postOwnerAction('http://localhost:3000', 'stop', undefined, async () => { calls++; })).ok, false);
  assert.equal(calls, 0);
  assert.equal((await postOwnerAction('https://localhost:3000', 'stop', undefined, async () => { calls++; return Response.json({}); })).ok, false);
  assert.equal(calls, 1);
});
test('unavailable Live screen has no paper balance, office, or activation button', () => {
  const html = render({ state: { kind: 'https-required' } });
  assert.match(html, /HTTPS/);
  assert.doesNotMatch(html, /฿0|iframe|เปิดเทรดเลย|รหัส API/);
});
test('disabled executor has one status without duplicate release instructions', () => {
  const html = render({ state: { kind: 'connected', status: { state: 'DISABLED', armed: false, reasons: ['EXECUTOR_NOT_CONFIGURED'] } } });
  assert.match(html, /ระบบเงินจริงยังไม่เปิด/);
  assert.doesNotMatch(html, /บัญชีสำหรับเทรดและระบบส่งคำสั่งยังไม่พร้อม|live-checklist/);
  assert.doesNotMatch(html, /฿[0-9]|iframe|พร้อมซื้อขาย/);
});
test('pairing form exposes checked browser remembering consent by default', () => {
  const html = render({ state: { kind: 'pair-required' }, code: 'x' });
  assert.match(html, /จำเบราว์เซอร์นี้สำหรับดูบัญชี 30 วัน/);
  assert.match(html, /type="checkbox"[^>]*checked/);
});
test('account pairing uses its own heading without the trading release checklist', () => {
  const html = render({ state: { kind: 'pair-required' }, accountOnly: true });
  assert.match(html, /เชื่อมบัญชี Bitkub/);
  assert.doesNotMatch(html, /สถานะการเทรด|รายละเอียดความพร้อมก่อนเริ่มใช้เงินจริง/);
  assert.match(html, /จำเบราว์เซอร์นี้สำหรับดูบัญชี 30 วัน/);
});

test('observation waits for its verified wallet before committing the account view', async () => {
  let finishWallet;
  const walletResponse = new Promise(resolve => { finishWallet = resolve; });
  let committed = false;
  const pending = readLiveObservation('https://localhost:3000', async url => {
    if (url.endsWith('/live/status')) return Response.json({ state: 'DISABLED', armed: false, reasons: ['EXECUTOR_NOT_CONFIGURED'] });
    assert.ok(url.endsWith('/live/wallet'));
    return walletResponse;
  }).then(result => { committed = true; return result; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(committed, false);
  finishWallet(Response.json({ source: 'Bitkub', observedAt: new Date().toISOString(), credentialReference: `sha256:${'a'.repeat(64)}`,
    balances: [{ currency: 'THB', available: 100, reserved: 0, total: 100 }], readAccessVerified: true,
    tradingEnabled: false, accountIdentityVerified: false }));
  const result = await pending;
  assert.equal(result.state.kind, 'connected');
  assert.equal(result.wallet.balances[0].available, 100);
});

test('failed authorization or wallet refresh removes amounts without inventing a zero', async () => {
  const unauthorized = await readLiveObservation('https://localhost:3000', async () => new Response(null, { status: 401 }));
  assert.deepEqual(unauthorized, { state: { kind: 'pair-required' } });
  const failedWallet = await readLiveObservation('https://localhost:3000', async url => url.endsWith('/live/status')
    ? Response.json({ state: 'DISABLED', armed: false, reasons: ['EXECUTOR_NOT_CONFIGURED'] })
    : new Response(null, { status: 503 }));
  assert.equal(failedWallet.wallet, undefined);
  assert.equal(failedWallet.account, undefined);
});
