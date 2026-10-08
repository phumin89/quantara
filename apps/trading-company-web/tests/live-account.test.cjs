const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const { validLiveAccount, readLiveAccount } = require('./load-component.cjs')(path.join(__dirname, '../app/liveAccount.ts'));
const { LiveAccountView, LiveOrdersView, LiveTradePlansView } = require('./load-component.cjs')(path.join(__dirname, '../app/LiveAccountPanel.tsx'));
const id = { value: '00000000-0000-0000-0000-000000000001' };
const now = Date.parse('2026-10-01T12:00:00Z');
const authority = { accountId: id, snapshotVersion: 5, mandateVersion: 2 };
const fixture = () => ({ accountId: id, brokerAccountReference: 'test-only-account',
  snapshot: { accountId: id, version: 5, observedAt: new Date(now).toISOString(), reconciled: true,
    cashBalance: 1000, cashReserved: 200, equity: 1100,
    positions: [{ symbol: 'BTC-THB', quantity: .001, reservedQuantity: 0, marketPrice: 100000 }] },
  mandate: { version: 2, capitalAllocation: 1000, maximumOrderNotional: 400, maximumDailyLoss: 100 },
  orders: [{ intentId: '00000000-0000-0000-0000-000000000002', symbol: 'BTC-THB', side: 'Buy', state: 'Unknown',
    quantity: .002, filledQuantity: 0, reservedCash: 200, reservedQuantity: 0, updatedAt: new Date(now).toISOString() }] });

test('only fresh reconciled account matching authoritative account and both versions is accepted', () => {
  assert.equal(validLiveAccount(fixture(), authority, now), true);
  for (const alter of [
    a => { a.accountId = { value: '00000000-0000-0000-0000-000000000003' }; },
    a => { a.snapshot.accountId = { value: '00000000-0000-0000-0000-000000000003' }; },
    a => { a.snapshot.version++; }, a => { a.mandate.version++; },
    a => { a.snapshot.reconciled = false; }, a => { a.snapshot.cashReserved = 1001; },
    a => { a.snapshot.equity = 999; }, a => { a.snapshot.cashBalance = Infinity; },
    a => { a.snapshot.observedAt = new Date(now - 30000).toISOString(); },
    a => { a.snapshot.observedAt = new Date(now + 1).toISOString(); },
    a => { a.snapshot.observedAt = '2026-02-31T12:00:00Z'; },
    a => { a.snapshot.positions.push(a.snapshot.positions[0]); },
    a => { a.orders[0].state = 'MadeUp'; }, a => { a.orders.push(a.orders[0]); },
    a => { a.orders[0].reservedCash = -1; }, a => { a.mandate.maximumDailyLoss = 0; },
  ]) { const a = fixture(); alter(a); assert.equal(validLiveAccount(a, authority, now), false); }
  assert.equal(validLiveAccount(fixture(), {}, now), false);
});
test('HTTP and unbound authority send no private account request', async () => {
  let calls = 0; const fetcher = async () => { calls++; throw Error('private-error'); };
  assert.equal(await readLiveAccount('http://localhost:3000', authority, fetcher), undefined);
  assert.equal(await readLiveAccount('https://localhost:3000', {}, fetcher), undefined);
  assert.equal(calls, 0);
});
test('private account uses fixed executor and cookies; malformed and unauthorized reads show no balance', async () => {
  for (const status of [401, 503, 200]) {
    let calls = 0;
    const result = await readLiveAccount('https://localhost:3000', authority, async (url, options) => {
      calls++; assert.equal(url, 'https://localhost:7443/live/account'); assert.equal(options.credentials, 'include');
      return Response.json({ snapshot: { equity: 100000 } }, { status });
    });
    assert.equal(result, undefined); assert.equal(calls, 1);
  }
});
test('a bound current account passes the actual private reader without mutation', async () => {
  const a = fixture();
  a.snapshot.observedAt = new Date().toISOString();
  a.orders[0].updatedAt = a.snapshot.observedAt;
  let calls = 0;
  const result = await readLiveAccount('https://localhost:3000', authority, async (url, options) => {
    calls++; assert.equal(options.method, undefined);
    return Response.json(a);
  });
  assert.deepEqual(result, a); assert.equal(calls, 1);
});
test('account and trading views preserve amounts, unknown outcome and reservations without activation', () => {
  const html = renderToStaticMarkup(React.createElement(LiveAccountView, { account: fixture() }));
  const orders = renderToStaticMarkup(React.createElement(LiveOrdersView, { account: fixture() }));
  assert.match(orders, /ยังยืนยันผลไม่ได้/); assert.match(orders, /฿200\.00/);
  assert.match(html, /฿800\.00/); assert.match(html, /฿200\.00/);
  assert.doesNotMatch(html, /เงินเดือน|iframe|เปิดเทรดเลย|test-only-account/);
  assert.doesNotMatch(orders, /เงินเดือน|iframe|เปิดเทรดเลย|test-only-account/);
});

const planFixture = () => ({ id: '00000000-0000-0000-0000-000000000005', symbol: 'ETH-THB',
  plan: { entryPrice: 100000, quantity: .004, takeProfitPrice: 110000, stopLossPrice: 95000, expiresAt: '2026-10-02T12:00:00Z' },
  status: 'WAITING_ENTRY', acquiredQuantity: 0, soldQuantity: 0 });
test('plans validate numeric geometry, identity, explicit time and actual fills; legacy remains accepted', () => {
  const a = fixture(); a.tradePlans = [planFixture()];
  assert.equal(validLiveAccount(a, authority, now), true);
  for (const alter of [p => p.plan.entryPrice = Infinity, p => p.plan.quantity = 0,
    p => p.plan.takeProfitPrice = 90000, p => p.plan.stopLossPrice = 100000,
    p => p.plan.expiresAt = '2026-10-02T12:00:00', p => p.plan.expiresAt = '2026-02-31T12:00:00Z',
    p => p.soldQuantity = .1, p => p.acquiredQuantity = -1, p => p.symbol = 'DOGE-THB', p => p.status = 'LIVE_PROFIT',
    p => p.id = 'not-an-id']) {
    a.tradePlans = [planFixture()]; alter(a.tradePlans[0]); assert.equal(validLiveAccount(a, authority, now), false);
  }
  a.tradePlans = [planFixture(), planFixture()]; assert.equal(validLiveAccount(a, authority, now), false);
  delete a.tradePlans; assert.equal(validLiveAccount(a, authority, now), true);
});
test('plan view distinguishes planned quantity and actual buy/sell with units and no activation action', () => {
  const a = fixture(); a.tradePlans = [planFixture()];
  const html = renderToStaticMarkup(React.createElement(LiveTradePlansView, { account: a }));
  assert.match(html, /รอราคาเข้า/); assert.match(html, /฿100,000\.00/);
  assert.match(html, /0\.004 ETH/); assert.match(html, /ซื้อได้จริง<\/dt><dd>0 ETH/);
  assert.match(html, /ขายแล้ว<\/dt><dd>0 ETH/);
  assert.doesNotMatch(html, /<button|<input|กำไร|00000000/);
  a.tradePlans[0].status = 'WATCHING_EXIT'; a.tradePlans[0].acquiredQuantity = .0039;
  const filled = renderToStaticMarkup(React.createElement(LiveTradePlansView, { account: a }));
  assert.match(filled, /ซื้อได้จริง<\/dt><dd>0\.0039 ETH/);
  delete a.tradePlans;
  assert.match(renderToStaticMarkup(React.createElement(LiveTradePlansView, { account: a })), /ยังไม่มีแผนซื้อขาย/);
});
