const assert = require('node:assert/strict');
const { buildSync } = require('esbuild');
const fs = require('node:fs');
const path = require('node:path');

fs.mkdirSync('work/quality', { recursive: true });
buildSync({
  entryPoints: ['apps/web/features/home/presentation/OfficeStateAdapter.ts'],
  bundle: true,
  platform: 'node',
  format: 'cjs',
  outfile: 'work/quality/home-adapter.cjs',
});
const { OfficeStateAdapter: adapter } = require(
  path.resolve('work/quality/home-adapter.cjs'),
);
const createRun = (id, role, status, at, accountKey = 'fixture-account-a') => ({
  id,
  accountKey,
  objective: 'Isolated fixture',
  status,
  createdAt: at,
  updatedAt: at,
  error: null,
  steps: [
    {
      role,
      status,
      output: status === 'completed' ? 'Recorded evidence' : null,
      error: null,
    },
    {
      role: 'reviewer',
      status: status === 'running' ? 'queued' : status,
      output: null,
      error: null,
    },
  ],
});
const completed = createRun(
  'ceo-old',
  'ceo',
  'completed',
  '2026-10-20T01:00:00Z',
);
const current = createRun(
  'intraday-new',
  'intraday',
  'running',
  '2026-10-20T02:00:00Z',
);
const foreign = createRun(
  'foreign',
  'news',
  'running',
  '2026-10-20T03:00:00Z',
  'fixture-account-b',
);
const overview = {
  configured: true,
  company: { enabled: true },
  latestRun: current,
};
const agents = adapter.project(overview, [completed, current, foreign], true);
const byRole = (role) => agents.find((agent) => agent.role === role);
assert.equal(agents.length, 8);
assert.deepEqual(
  agents.slice(0, 3).map((agent) => [agent.role, agent.id]),
  [
    ['ceo', 1],
    ['analyst', 2],
    ['reviewer', 3],
  ],
);
assert.equal(byRole('intraday').status, 'active');
assert.equal(byRole('reviewer').status, 'queued');
assert.equal(byRole('ceo').status, 'completed');
assert.equal(byRole('analyst').status, 'idle');
assert.equal(
  byRole('news').status,
  'idle',
  'Foreign account work must not enter this office.',
);
assert.equal(
  adapter
    .project({ ...overview, company: { enabled: false } }, [], true)
    .find((agent) => agent.role === 'intraday').status,
  'blocked',
);
assert.equal(
  adapter
    .project(overview, [completed], false)
    .find((agent) => agent.role === 'intraday').status,
  'stale',
);
assert(
  adapter.project(null, [], false).every((agent) => agent.status === 'unknown'),
);
const contradictory = { ...current, status: 'failed' };
assert.equal(
  adapter
    .project({ ...overview, latestRun: contradictory }, [], true)
    .find((agent) => agent.role === 'intraday').status,
  'unknown',
);
for (const status of ['failed', 'cancelled', 'completed', 'queued']) {
  const run = createRun(
    'status-fixture',
    'intraday',
    status,
    current.updatedAt,
  );
  assert.equal(
    adapter
      .project({ ...overview, latestRun: run }, [], true)
      .find((agent) => agent.role === 'intraday').status,
    status,
  );
}
const duplicate = adapter.project(overview, [current, current], true);
assert.equal(
  adapter
    .project(
      { ...overview, latestRun: { ...current, accountKey: null } },
      [foreign],
      true,
    )
    .find((agent) => agent.role === 'news').runId,
  null,
  'Legacy unidentified accounts cannot merge identified accounts.',
);
assert.deepEqual(
  duplicate,
  adapter.project(overview, [current], true),
  'Snapshot replay must be idempotent.',
);
assert.equal(
  adapter.isOlder(overview, {
    ...overview,
    latestRun: { ...current, updatedAt: '2026-10-20T01:59:00Z' },
  }),
  true,
);
assert.equal(
  adapter.isOlder(overview, { ...overview, latestRun: completed }),
  true,
);
assert.equal(
  adapter.isOlder(overview, { ...overview, latestRun: null }),
  false,
);
assert.equal(
  adapter.isOlder(overview, {
    ...overview,
    latestRun: {
      ...completed,
      accountKey: 'fixture-account-b',
    },
  }),
  false,
  'A new account is not an out-of-order response.',
);
assert(
  adapter
    .project({ ...overview, latestRun: null }, [current], true)
    .every((agent) => agent.runId === null),
  'No current run cannot identify retained account evidence.',
);
assert.equal(
  adapter.isOlder(overview, {
    ...overview,
    latestRun: { ...current, status: 'completed' },
  }),
  true,
  'Conflicting duplicate revisions are refused.',
);
assert.equal(adapter.isOlder(overview, overview), false);
assert.equal(
  adapter.isOlder(overview, {
    ...overview,
    latestRun: { ...current, updatedAt: '2026-10-20T02:01:00Z' },
  }),
  false,
);
assert.equal(adapter.clean('x\u0000y\n' + 'z'.repeat(900)).length, 700);
assert(!adapter.clean('x\u0000y').includes('\u0000'));
assert.equal(adapter.date('2026-10-19T18:00:00Z'), '2026-10-20');
console.log(
  'Home adapter: actual step states, stable seats, account isolation, stale/unknown, stopped work, duplicate/out-of-order/conflicting snapshots, bounded text and Bangkok day passed.',
);
