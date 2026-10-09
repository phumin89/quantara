const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const load = require('./load-component.cjs');
const { validLiveActivity, validLiveIssues, readLiveActivity } = load(path.join(__dirname, '../app/liveActivity.ts'));
const { LiveOffice } = load(path.join(__dirname, '../app/LiveOffice.tsx'));
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const account = 'ca692cd6-de98-4146-aa92-7cbb2a0df92a';
const make = now => ({ accountContext: { mode: 1, accountId: account.replaceAll('-', '') },
  meetingId: 'd79eb810-8e2b-4539-bfe7-bd82e00429fb', observedAt: new Date(now).toISOString(),
  roles: [{ role: 'ceo', status: 'working', changedAt: new Date(now).toISOString() }] });

test('working roles require fresh, account-bound real meeting activity', () => {
  const now = Date.parse('2026-10-06T06:00:00Z');
  const activity = make(now);
  assert.equal(validLiveActivity(activity, account, now), true);
  assert.equal(validLiveActivity(activity, account, now + 45000), false);
  for (const invalid of [
    { ...activity, accountContext: { ...activity.accountContext, mode: 0 } },
    { ...activity, accountContext: { ...activity.accountContext, accountId: 'a'.repeat(32) } },
    { ...activity, meetingId: null },
    { ...activity, roles: [...activity.roles, ...activity.roles] },
    { ...activity, roles: [...activity.roles, { ...activity.roles[0], role: 'market-research' }] },
    { ...activity, roles: [{ ...activity.roles[0], status: 'trading' }] },
    make(now + 5001),
  ]) assert.equal(validLiveActivity(invalid, account, now), false);
  assert.equal(validLiveActivity({ ...activity, meetingId: null, roles: [] }, account, now), true);
});

test('activity reads use viewing credentials and clear failures without fetching on a foreign origin', async () => {
  const activity = make(Date.now());
  let calls = 0;
  const fetcher = async (url, options) => {
    calls++;
    assert.equal(url, 'https://localhost:7443/live/activity');
    assert.equal(options.credentials, 'include');
    return new Response(JSON.stringify({ activity }), { status: 200 });
  };
  assert.deepEqual(await readLiveActivity('https://localhost:3000', account, fetcher), activity);
  assert.equal(await readLiveActivity('http://localhost:3000', account, fetcher), undefined);
  assert.equal(calls, 1);
  assert.equal(await readLiveActivity('https://localhost:3000', account, async () => new Response('', { status: 401 })), undefined);
});
test('optional coordinator status and symbols are validated and rendered', () => {
  const now = Date.parse('2026-10-06T06:00:00Z');
  const valid = { ...make(now), coordinatorStatus: 'WATCHING', researchSymbols: ['BTC-THB', 'ETH-THB'], tradingSymbols: ['BTC-THB'] };
  assert.equal(validLiveActivity(valid, account, now), true);
  for (const invalid of [
    { ...valid, coordinatorStatus: 'watching' }, { ...valid, researchSymbols: ['BTC-THB', 'BTC-THB'] },
    { ...valid, researchSymbols: ['DOGE-USDT'] }, { ...valid, tradingSymbols: ['SOL-THB'] },
    { ...valid, researchSymbols: undefined },
  ]) assert.equal(validLiveActivity(invalid, account, now), false);
  assert.equal(validLiveActivity({ ...make(now), tradingSymbols: [] }, account, now), true);
  assert.equal(validLiveActivity(make(now), account, now), true);
  const html = renderToStaticMarkup(React.createElement(LiveOffice, { activity: { ...valid, roles: [{ ...valid.roles[0], status: 'completed' }] } }));
  assert.ok(html.includes('เฝ้าตลาด')); assert.ok(html.includes('วิเคราะห์ BTC · ETH')); assert.ok(html.includes('เทรดได้ BTC'));
});
test('activity and office accept dynamic THB spot markets with more than three research symbols', () => {
  const now = Date.parse('2026-10-06T06:00:00Z');
  const researchSymbols = ['BTC-THB', 'ETH-THB', 'DOGE-THB', 'XRP-THB', '1-THB', `${'A'.repeat(15)}-THB`];
  const activity = { ...make(now), researchSymbols, tradingSymbols: ['DOGE-THB', 'XRP-THB'],
    issues: [{ code: 'MARKET_HISTORY_UNAVAILABLE', symbol: 'DOGE-THB' }, { code: 'MARKET_HISTORY_UNAVAILABLE', symbol: 'XRP-THB' }] };
  assert.equal(validLiveActivity(activity, account, now), true);
  const html = renderToStaticMarkup(React.createElement(LiveOffice, { activity }));
  assert.ok(html.includes('วิเคราะห์ BTC · ETH · DOGE · XRP'));
  assert.ok(html.includes('เทรดได้ DOGE · XRP'));
  const atLimit = { ...make(now), researchSymbols: Array.from({ length: 1000 }, (_, i) => `C${i}-THB`) };
  atLimit.tradingSymbols = atLimit.researchSymbols;
  assert.equal(validLiveActivity(atLimit, account, now), true);
  assert.equal(validLiveActivity({ ...atLimit, researchSymbols: [...atLimit.researchSymbols, 'C1000-THB'] }, account, now), false);
  assert.equal(validLiveActivity({ ...atLimit, tradingSymbols: [...atLimit.tradingSymbols, 'C1000-THB'] }, account, now), false);
});
test('activity and office reject non-canonical symbols, duplicates and invalid array shapes', () => {
  const now = Date.parse('2026-10-06T06:00:00Z');
  const invalidSymbols = [null, 1, '', '-THB', 'THB-THB', 'DOGE-USDT', 'doge-THB', 'DOGE-thb',
    'DÖGE-THB', 'DOGE_THB', 'DOGE-THB?x=1', 'DOGE-THB/..', '../DOGE-THB', `${'A'.repeat(16)}-THB`,
    ' DOGE-THB', 'DOGE-THB ', 'DOGE-THB\n', 'DOGE-THB\r', 'DOGE-THB\u2028'];
  for (const symbol of invalidSymbols) {
    assert.equal(validLiveIssues([{ code: 'MARKET_HISTORY_UNAVAILABLE', symbol }]), false);
  }
  assert.equal(validLiveIssues([{ code: 'MARKET_HISTORY_UNAVAILABLE', symbol: 'DOGE-THB' }, { code: 'MARKET_HISTORY_UNAVAILABLE', symbol: 'DOGE-THB' }]), false);
  for (const researchSymbols of [null, {}, ['DOGE-THB', 'DOGE-THB'], ...invalidSymbols.map(symbol => [symbol])]) {
    const activity = { ...make(now), researchSymbols };
    assert.equal(validLiveActivity(activity, account, now), false);
    assert.equal(validLiveActivity({ ...make(now), researchSymbols: ['DOGE-THB'], tradingSymbols: researchSymbols }, account, now), false);
    const html = renderToStaticMarkup(React.createElement(LiveOffice, { activity }));
    assert.ok(!html.includes('วิเคราะห์ '));
  }
});

test('eight distinct native market history gaps survive activity and retain a bounded issue list', () => {
  const now = Date.parse('2026-10-06T06:00:00Z');
  const issues = ['BTC', 'ETH', 'SOL', 'DOGE', 'XRP', 'ADA', 'USDT', 'NEAR'].map(base => ({ code: 'MARKET_HISTORY_UNAVAILABLE', symbol: `${base}-THB` }));
  assert.equal(validLiveIssues(issues), true);
  assert.equal(validLiveActivity({ ...make(now), issues }, account, now), true);
  const atLimit = Array.from({ length: 1000 }, (_, i) => ({ code: 'MARKET_HISTORY_UNAVAILABLE', symbol: `C${i}-THB` }));
  assert.equal(validLiveIssues(atLimit), true);
  assert.equal(validLiveIssues([...atLimit, { code: 'MARKET_HISTORY_UNAVAILABLE', symbol: 'C1000-THB' }]), false);
});

test('activity issue metadata fails closed and preserves legacy records', () => {
  const now = Date.parse('2026-10-06T06:00:00Z');
  const valid = { ...make(now), issues: [{ code: 'MARKET_HISTORY_UNAVAILABLE', symbol: 'BTC-THB' }, { code: 'MEETING_FAILED' }], nextRetryAt: new Date(now + 86400000).toISOString() };
  assert.equal(validLiveIssues(valid.issues), true);
  assert.equal(validLiveActivity(valid, account, now), true);
  assert.equal(validLiveActivity(make(now), account, now), true);
  for (const issues of [
    [{ code: 'UNKNOWN' }], [{ code: 'MARKET_HISTORY_UNAVAILABLE' }],
    [{ code: 'MEETING_FAILED', symbol: 'BTC-THB' }],
    [{ code: 'MEETING_FAILED' }, { code: 'MEETING_FAILED' }],
    Array.from({ length: 6 }, () => ({ code: 'MEETING_FAILED' })),
  ]) assert.equal(validLiveActivity({ ...make(now), issues }, account, now), false);
  assert.equal(validLiveActivity({ ...valid, nextRetryAt: new Date(now + 86400001).toISOString() }, account, now), false);
  assert.equal(validLiveActivity({ ...valid, nextRetryAt: new Date(now - 86400001).toISOString() }, account, now), false);
});
test('working activity hides stale saved issues and retry until the role finishes', () => {
  const now = Date.parse('2026-10-06T06:00:00Z');
  const activity = { ...make(now), coordinatorStatus: 'LIVE_BLOCKED', issues: [{ code: 'ACCOUNT_STATE_INVALID' }], nextRetryAt: new Date(now + 3600000).toISOString() };
  const render = value => renderToStaticMarkup(React.createElement(LiveOffice, { activity: value }));
  const working = render(activity);
  assert.ok(working.includes('CEO กำลังวิเคราะห์'));
  assert.ok(!working.includes('สถานะบัญชีไม่ถูกต้อง'));
  assert.ok(!working.includes('ตรวจอีกครั้ง'));
  const completed = render({ ...activity, roles: [{ ...activity.roles[0], status: 'completed' }] });
  assert.ok(completed.includes('สถานะบัญชีไม่ถูกต้อง'));
  assert.ok(completed.includes('ตรวจอีกครั้ง'));
});
test('office distinguishes configured staff from actual working roles', () => {
  const activity = { ...make(Date.parse('2026-10-07T07:00:00Z')), coordinatorStatus: 'READ_ONLY', roles: [{ role: 'market-research', status: 'failed', changedAt: '2026-10-07T07:00:00Z' }] };
  const render = value => renderToStaticMarkup(React.createElement(LiveOffice, { activity: value }));
  const idle = render(activity);
  assert.ok(idle.includes('ทีม 8 บทบาท'));
  assert.ok(idle.includes('เฝ้าตลาด'));
  assert.ok(!idle.includes('กำลังทำงาน'));
  assert.ok(render({ ...activity, roles: [{ ...activity.roles[0], status: 'working' }] }).includes('กำลังทำงาน 1'));
  assert.ok(render({ ...activity, coordinatorStatus: undefined, researchSymbols: ['BTC-THB'] }).includes('ทีม 8 บทบาท · วิเคราะห์ BTC'));
  assert.ok(!render(undefined).includes('ทีม 8 บทบาท'));
});
