const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

require.extensions['.ts'] = (module, filename) => {
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  });
  module._compile(result.outputText, filename);
};

const { DashboardPresentation } = require(
  path.join(__dirname, '../presentation/DashboardPresentation.ts'),
);
const now = Date.parse('2026-10-20T18:00:00Z');
assert.equal(
  DashboardPresentation.date('2026-10-21', now),
  true,
  'The selected day uses Bangkok midnight.',
);
assert.equal(DashboardPresentation.date('2026-10-22', now), false);
assert.equal(DashboardPresentation.date('2026-02-30', now), false);
assert.equal(DashboardPresentation.money(null), 'Unavailable');
assert.notEqual(DashboardPresentation.money(0), 'Unavailable');

const report = {
  mode: 'autoLive',
  date: '2026-10-20',
  revision: 1,
  realizedNetThb: '-12.25',
  createdAt: '2026-10-20T10:00:00Z',
};
const trading = {
  accountKey: 'current-account',
  rulesHash: 'rules',
  mode: 'paper',
  mandate: null,
  emergencyStopped: false,
  fault: null,
  heartbeatAt: '2026-10-20T18:00:00Z',
  live: { orders: [] },
  reports: [
    report,
    { ...report, revision: 3, realizedNetThb: '5.25' },
    { ...report, mode: 'paper', revision: 9, realizedNetThb: '99999' },
  ],
  autonomy: {
    observedAt: '2026-10-20T18:00:00Z',
    funnel: {
      date: '2026-10-20',
      netThb: '99999',
      filledOrders: 100,
      closedTrades: 50,
    },
  },
};
const historical = DashboardPresentation.day(trading, '2026-10-20');
assert.equal(
  historical.realizedNet,
  5.25,
  'Paper results cannot replace the real engine report.',
);
assert.equal(
  historical.filledOrders,
  null,
  'Missing historical counts stay unavailable.',
);
assert.equal(historical.closedTrades, null);
assert.match(historical.source, /revision 3/);
assert.equal(
  DashboardPresentation.day(trading, '2026-10-19').realizedNet,
  null,
  'A different day cannot inherit profits.',
);
assert.equal(DashboardPresentation.day(null, '2026-10-20').realizedNet, null);
const live = {
  ...trading,
  mode: 'autoLive',
  autonomy: {
    ...trading.autonomy,
    funnel: {
      ...trading.autonomy.funnel,
      netThb: null,
      filledOrders: 2,
      closedTrades: 1,
    },
  },
};
assert.equal(
  DashboardPresentation.day(live, '2026-10-20').realizedNet,
  null,
  'Unknown current evidence cannot fall back to a known older report.',
);
assert.equal(DashboardPresentation.day(live, '2026-10-20').filledOrders, 2);

const activity = {
  now,
  fetchedAt: now,
  error: null,
  overview: { configured: true, company: { status: 'waiting', error: null } },
};
const reportId = 'cbce21f9-4971-4581-a2ec-6aec92071af0';
assert.equal(
  DashboardPresentation.researchHref('2026-10-20', reportId),
  `/daily-brief?date=2026-10-20&view=team-chat#run-${reportId}`,
  'The report link selects the real Team chat tab and recorded run anchor.',
);
assert.equal(
  DashboardPresentation.researchHref('2026-10-20'),
  '/daily-brief?date=2026-10-20&view=team-chat',
);
assert.equal(DashboardPresentation.researchError(activity), null);
assert.equal(
  DashboardPresentation.researchError({
    ...activity,
    error: 'Latest refresh failed.',
  }),
  'Latest refresh failed.',
  'A retained current-day brief must preserve the failed refresh status.',
);
assert.match(
  DashboardPresentation.researchError({ ...activity, fetchedAt: now - 31000 }),
  /stale/i,
  'Current-day records cannot appear fresh once the shared evidence is stale.',
);
const issues = DashboardPresentation.issues(null, live, activity);
assert.equal(
  issues.some((issue) => issue.key === 'mandate'),
  true,
  'AutoLive with missing authority needs attention.',
);
assert.equal(
  issues.some((issue) => issue.key === 'team-error'),
  false,
  'A regular evidence wait is not a current failure.',
);
const uncertain = { ...live, live: { orders: [{ status: 'unknown' }] } };
assert.equal(
  DashboardPresentation.issues(null, uncertain, activity).some(
    (issue) => issue.key === 'uncertain-orders',
  ),
  true,
);
const stale = { ...activity, fetchedAt: now - 31000 };
assert.equal(
  DashboardPresentation.issues(null, null, stale).some(
    (issue) => issue.key === 'team-stale',
  ),
  true,
);
const failed = {
  ...activity,
  overview: {
    configured: true,
    company: { status: 'waitingForPortfolio', error: 'Portfolio unavailable' },
  },
};
assert.equal(
  DashboardPresentation.issues(null, null, failed).some(
    (issue) => issue.key === 'team-error',
  ),
  true,
);

console.log(
  'Dashboard checks passed: Bangkok dates, exact-day engine scope, paper isolation, unknown results, retained revisions and actionable status.',
);
