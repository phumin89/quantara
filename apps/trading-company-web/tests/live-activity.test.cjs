const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const load = require('./load-component.cjs');
const { validLiveActivity, readLiveActivity } = load(path.join(__dirname, '../app/liveActivity.ts'));
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
