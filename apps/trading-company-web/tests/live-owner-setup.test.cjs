const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { readOwnerSetup, fullSpotMandate, ownerMutation } = require('./load-component.cjs')(path.join(__dirname, '../app/ownerSetupClient.ts'));

const accountId = { value: '11111111-1111-4111-8111-111111111111' };
const base = () => ({ configured: true, canControl: true, reasons: [], accountId, brokerAccountReference: 'bound', verifiedSymbols: ['BTC-THB', 'ETH-THB'], availableCapital: 12500, fullSpotCapitalAllowed: true, minimumFeeReserveRate: .001, provisioned: false, mandate: null, snapshotVersion: null, mandateVersion: null, baselineEstablished: false, needsDailyBaseline: false, armed: false, stopped: false, reconciled: false, observedAt: null });

test('malformed setup metadata refuses consent and missing or zero capital cannot build policy', () => {
  const invalid = base(); invalid.accountId = '11111111-1111-4111-8111-111111111111';
  assert.equal(readOwnerSetup(invalid), undefined);
  const missing = base(); delete missing.availableCapital;
  assert.equal(readOwnerSetup(missing), undefined);
  const zero = base(); zero.availableCapital = 0;
  assert.ok(readOwnerSetup(zero));
  assert.equal(fullSpotMandate(missing), undefined);
  assert.equal(fullSpotMandate(zero), undefined);
});

test('full spot policy uses actual capital, object account id, verified symbols, and daily rollover', () => {
  const policy = fullSpotMandate(base());
  assert.deepEqual(policy, { accountId, expectedVersion: 0, capitalAllocation: 12500, maximumOrderNotional: 12500, maximumDailyTurnover: 12500, maximumDailyLoss: 12500, maximumDrawdown: 1, maximumPortfolioExposure: 1, maximumOpenPositions: 2, maximumSlippage: .02, feeReserveRate: .001, allowedSymbols: ['BTC-THB', 'ETH-THB'], expiresAt: null, automaticDailyRollover: true });
});

test('setup and existing mandate accept canonical verified THB spot markets up to the defensive limit', () => {
  for (const verifiedSymbols of [
    ['BTC-THB', 'ETH-THB', 'DOGE-THB', 'XRP-THB', '1-THB', `${'A'.repeat(15)}-THB`],
    Array.from({ length: 1000 }, (_, i) => `C${i}-THB`),
  ]) {
    const setup = { ...base(), verifiedSymbols };
    assert.deepEqual(readOwnerSetup(setup), setup);
    const policy = fullSpotMandate(setup);
    assert.deepEqual(policy.allowedSymbols, verifiedSymbols);
    assert.equal(policy.maximumOpenPositions, verifiedSymbols.length);
    const provisioned = { ...setup, provisioned: true, snapshotVersion: 1, mandateVersion: 1,
      observedAt: '2026-10-09T00:00:00Z', mandate: { ...policy, version: 1 } };
    assert.deepEqual(readOwnerSetup(provisioned), provisioned);
  }
});
test('verified and allowed symbols reject malformed, duplicate, null and oversized input', () => {
  const invalidSymbols = [null, 1, '', '-THB', 'THB-THB', 'DOGE-USDT', 'doge-THB', 'DOGE-thb',
    'DÖGE-THB', 'DOGE_THB', 'DOGE-THB?x=1', 'DOGE-THB/..', '../DOGE-THB', `${'A'.repeat(16)}-THB`,
    ' DOGE-THB', 'DOGE-THB ', 'DOGE-THB\n', 'DOGE-THB\r', 'DOGE-THB\u2028'];
  const invalidLists = [null, {}, [], ['DOGE-THB', 'DOGE-THB'],
    Array.from({ length: 1001 }, (_, i) => `C${i}-THB`), ...invalidSymbols.map(symbol => [symbol])];
  for (const verifiedSymbols of invalidLists) {
    const setup = { ...base(), verifiedSymbols };
    assert.equal(readOwnerSetup(setup), undefined);
    assert.equal(fullSpotMandate(setup), undefined);
    const provisioned = { ...base(), provisioned: true, snapshotVersion: 1, mandateVersion: 1,
      observedAt: '2026-10-09T00:00:00Z', mandate: { ...fullSpotMandate(base()), version: 1, allowedSymbols: verifiedSymbols } };
    assert.equal(readOwnerSetup(provisioned), undefined);
  }
});

test('mutations use fixed HTTPS origin, CSRF, allowlist, and one POST without retry', async () => {
  const calls = [];
  const fetcher = async (url, options) => { calls.push([url, options]); if (url.endsWith('/owner/csrf')) return Response.json({ token: 'csrf' }); throw new Error('ambiguous'); };
  assert.equal((await ownerMutation('http://localhost:3000', '/live/provision', {} , fetcher)).status, 0);
  assert.equal(calls.length, 0);
  await assert.rejects(() => ownerMutation('https://localhost:3000', '/live/provision', { accountId }, fetcher));
  assert.equal(calls.length, 2);
  assert.equal(calls[1][0], 'https://localhost:7443/live/provision');
  assert.equal(calls[1][1].method, 'POST');
  assert.equal(calls[1][1].headers['X-CSRF-TOKEN'], 'csrf');
});
test('capital changes and a new Stop require renewed displayed owner consent',()=>{
  const {sameOwnerConsent}=require('./load-component.cjs')(path.join(__dirname,'../app/ownerSetupClient.ts'));
  const current=base();
  assert.equal(sameOwnerConsent(current,{...current,snapshotVersion:5}),true);
  for(const changed of [{availableCapital:13000},{stopped:true},{mandateVersion:2},{brokerAccountReference:'other'},{minimumFeeReserveRate:.003},{verifiedSymbols:['BTC-THB']}])assert.equal(sameOwnerConsent(current,{...current,...changed}),false);
});
