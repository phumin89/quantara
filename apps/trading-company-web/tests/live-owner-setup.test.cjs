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
