const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const loaded = require('./load-component.cjs')(path.join(__dirname, '../app/bitkubWallet.ts'));
const wallet = { source: 'Bitkub', observedAt: new Date().toISOString(), credentialReference: `sha256:${'a'.repeat(64)}`, balances: [{ currency: 'THB', available: 0, reserved: 0, total: 0 }], readAccessVerified: true, tradingEnabled: false, accountIdentityVerified: false };
test('validates truthful zero THB wallet', () => assert.equal(loaded.validBitkubWallet(wallet), true));
test('does not fetch on HTTP origin', async () => { let calls = 0; assert.equal(await loaded.readBitkubWallet('http://localhost:3000', async () => { calls++; }), undefined); assert.equal(calls, 0); });
test('rejects stale and inconsistent wallets', () => { assert.equal(loaded.validBitkubWallet({ ...wallet, observedAt: new Date(Date.now() - 30000).toISOString() }), false); assert.equal(loaded.validBitkubWallet({ ...wallet, balances: [{ ...wallet.balances[0], total: 1 }] }), false); });

test('accepts actual .NET DateTimeOffset fractions and offsets and numeric coin symbols', () => {
  const now = Date.parse('2026-10-05T07:00:01Z');
  assert.equal(loaded.validBitkubWallet({ ...wallet, observedAt: '2026-10-05T07:00:00.1234567+00:00', balances: [...wallet.balances, {currency:'1INCH',available:1,reserved:0,total:1}] }, now), true);
});
test('rejects future, impossible dates, duplicate balances and any trading authority claim', () => {
  const now = Date.parse('2026-10-05T07:00:01Z');
  for (const changed of [{observedAt:'2026-10-05T07:00:02Z'}, {observedAt:'2026-02-30T00:00:00Z'},
    {balances:[...wallet.balances,...wallet.balances]}, {tradingEnabled:true}, {accountIdentityVerified:true}])
    assert.equal(loaded.validBitkubWallet({...wallet,observedAt:'2026-10-05T07:00:00Z',...changed}, now), false);
});
test('private reader never turns provider failure into a zero balance', async () => {
  for (const status of [401,503]) assert.equal(await loaded.readBitkubWallet('https://localhost:3000', async () => new Response('{}',{status})), undefined);
  assert.equal(await loaded.readBitkubWallet('https://localhost:3000', async () => {throw Error('private provider error');}), undefined);
});
test('wallet render labels zero amounts and does not imply activation or expose credential reference', () => {
  const React = require('react'), {renderToStaticMarkup} = require('react-dom/server');
  const {BitkubWalletView} = require('./load-component.cjs')(path.join(__dirname,'../app/LiveAccountPanel.tsx'));
  const html = renderToStaticMarkup(React.createElement(BitkubWalletView,{wallet}));
  assert.match(html,/อ่านข้อมูลเท่านั้น/);
  assert.match(html,/0\.00/);
  assert.match(html,/ยังไม่ยืนยันบัญชีสำหรับส่งคำสั่งหรือเปิดการเทรด/);
  assert.doesNotMatch(html,/sha256:|เปิดใช้งานการเทรด|<input/);
});
