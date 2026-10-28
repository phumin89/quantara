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
const {
  DashboardFinancials,
} = require('../presentation/DashboardFinancials.ts');
const observedNow = Date.parse('2026-10-20T03:00:00Z');
const risky = {
  policy: { maximumDataAgeSeconds: 90 },
  capital: {
    observedAt: '2026-10-20T03:00:00Z',
    equityThb: '1000',
    blocker: 'Incomplete source',
    hasUncertainOrders: false,
  },
  performance: {
    from: '2026-10-19T03:00:00Z',
    to: '2026-10-20T03:00:00Z',
    timeWeightedReturn: '0.01',
  },
};
const objective = {
  objective: {
    valuationStatus: 'verified',
    observedAt: '2026-10-20T03:00:00Z',
    portfolioValueThb: '9999',
  },
};
assert.equal(
  DashboardFinancials.capital(risky, objective, observedNow),
  null,
  'A newer blocked capital observation cannot fall back to a different verified balance.',
);
const complete = { ...risky, capital: { ...risky.capital, blocker: null } };
assert.equal(
  DashboardFinancials.capital(complete, objective, observedNow),
  1000,
);
assert.equal(
  DashboardFinancials.capital(complete, objective, observedNow + 91000),
  null,
  'The actual source freshness policy controls verified capital.',
);
assert.equal(
  DashboardFinancials.capitalCaption(complete, objective, observedNow),
  'Current verified broker valuation',
);
assert.equal(
  DashboardFinancials.capitalCaption(risky, objective, observedNow),
  'Incomplete source',
);
assert.equal(
  DashboardFinancials.capitalCaption(
    { ...complete, capital: { ...complete.capital, hasUncertainOrders: true } },
    objective,
    observedNow,
  ),
  'An order outcome is unconfirmed. Review orders in Crypto.',
);
assert.equal(
  DashboardFinancials.capitalCaption(complete, objective, observedNow + 91000),
  'The latest account valuation is out of date.',
);
assert.equal(
  DashboardFinancials.capitalCaption(null, null, observedNow),
  'No verified account valuation is available.',
);
assert.equal(
  DashboardFinancials.returnCaption(null, observedNow),
  'Return needs a current, verified account valuation.',
);
assert.equal(
  DashboardFinancials.returnCaption(
    { ...complete, capital: null },
    observedNow,
  ),
  'Return needs a current, verified account valuation.',
);
assert.equal(DashboardFinancials.accountReturn(complete, observedNow), 0.01);
assert.equal(
  DashboardFinancials.returnCaption(complete, observedNow),
  'Recorded interval · adjusted for flows',
);
assert.equal(
  DashboardFinancials.returnCaption(complete, observedNow + 91000),
  'Return needs a current, verified account valuation.',
);
assert.equal(
  DashboardFinancials.returnCaption(
    {
      ...complete,
      performance: { ...complete.performance, to: '2026-10-20T02:59:59Z' },
    },
    observedNow,
  ),
  'Return is unavailable for the latest valuation.',
);
assert.equal(
  DashboardFinancials.returnCaption(
    {
      ...complete,
      performance: { ...complete.performance, from: '2026-10-20T04:00:00Z' },
    },
    observedNow,
  ),
  'Return is unavailable for the latest valuation.',
);
assert.equal(
  DashboardFinancials.returnCaption(
    { ...complete, performance: null },
    observedNow,
  ),
  'No complete return interval has been recorded.',
);
assert.equal(
  DashboardFinancials.returnCaption(
    {
      ...complete,
      performance: {
        ...complete.performance,
        timeWeightedReturn: null,
        explanation: 'Transfer evidence is incomplete.',
      },
    },
    observedNow,
  ),
  'Transfer evidence is incomplete.',
);
assert.equal(
  DashboardFinancials.returnCaption(
    {
      ...complete,
      performance: {
        ...complete.performance,
        timeWeightedReturn: null,
        explanation: '   ',
      },
    },
    observedNow,
  ),
  'No complete return interval has been recorded.',
);
assert.equal(
  DashboardFinancials.accountReturn(
    {
      ...complete,
      performance: { ...complete.performance, timeWeightedReturn: null },
    },
    observedNow,
  ),
  null,
);
assert.equal(
  DashboardFinancials.accountReturn(
    {
      ...complete,
      performance: { ...complete.performance, to: '2026-10-20T02:59:59Z' },
    },
    observedNow,
  ),
  null,
  'A return must match the observed closing capital watermark.',
);
const graphSource = {
  mode: 'paper',
  reports: [
    {
      date: '2026-10-19',
      mode: 'autoLive',
      revision: 1,
      endingEquityThb: '1000',
      realizedNetThb: '-5',
      createdAt: '2026-10-19T02:00:00Z',
    },
    {
      date: '2026-10-19',
      mode: 'autoLive',
      revision: 2,
      endingEquityThb: '990',
      realizedNetThb: '0',
      createdAt: '2026-10-19T03:00:00Z',
    },
    {
      date: '2026-10-19',
      mode: 'paper',
      revision: 3,
      endingEquityThb: '99999',
      realizedNetThb: '99999',
      createdAt: '2026-10-19T04:00:00Z',
    },
  ],
  live: { equityThb: '900' },
  heartbeatAt: '2026-10-20T02:00:00Z',
};
const series = DashboardFinancials.series(
  graphSource,
  '2026-10-20',
  'realizedNetThb',
  observedNow,
);
assert.equal(
  series.find((point) => point.label === '2026-10-19').value,
  0,
  'Zero is a known outcome; the latest live revision wins without mixing Paper.',
);
assert.equal(
  series.find((point) => point.label === '2026-10-18').value,
  null,
  'Missing dates stay gaps.',
);
assert.equal(
  DashboardFinancials.series(
    { ...graphSource, mode: 'autoLive' },
    '2026-10-20',
    'endingEquityThb',
    observedNow,
  ).at(-1).value,
  null,
  'Stale current engine values cannot be placed on today as a fresh equity observation.',
);
const stoppedCurrent = {
  ...graphSource,
  mode: 'stopped',
  live: { equityThb: '777' },
  heartbeatAt: '2026-10-20T02:59:30Z',
  autonomy: { funnel: { date: '2026-10-20', netThb: '42' } },
  reports: [
    {
      date: '2026-10-20',
      mode: 'autoLive',
      revision: 1,
      endingEquityThb: '700',
      realizedNetThb: '5',
      createdAt: '2026-10-20T01:00:00Z',
    },
  ],
};
assert.equal(
  DashboardFinancials.series(
    stoppedCurrent,
    '2026-10-20',
    'realizedNetThb',
    observedNow,
  ).at(-1).value,
  42,
  'Stopped current-day funnel evidence must override an older report revision.',
);
assert.equal(
  DashboardFinancials.series(
    stoppedCurrent,
    '2026-10-20',
    'endingEquityThb',
    observedNow,
  ).at(-1).value,
  777,
  'Stopped current-day equity must use the live ledger snapshot.',
);
assert.equal(
  DashboardFinancials.series(
    { ...stoppedCurrent, heartbeatAt: '2026-10-19T23:00:00Z' },
    '2026-10-20',
    'realizedNetThb',
    observedNow,
  ).at(-1).value,
  null,
  'A stale stopped heartbeat leaves the current-day result unknown.',
);
for (const mode of ['stopped', 'manualReview', 'autoLive']) {
  const current = {
    ...stoppedCurrent,
    mode,
    autonomy: { funnel: { date: '2026-10-20', netThb: '12', filledOrders: 2 } },
  };
  assert.equal(
    DashboardFinancials.series(
      current,
      '2026-10-20',
      'realizedNetThb',
      observedNow,
    ).at(-1).value,
    12,
    `${mode} uses matching current funnel evidence.`,
  );
  assert.equal(
    DashboardFinancials.series(
      {
        ...current,
        autonomy: { funnel: { date: '2026-10-20', netThb: null } },
      },
      '2026-10-20',
      'realizedNetThb',
      observedNow,
    ).at(-1).value,
    null,
    `${mode} preserves unknown current funnel evidence.`,
  );
  for (const heartbeatAt of ['2026-10-19T23:00:00Z', '2026-10-20T04:00:01Z']) {
    assert.equal(
      DashboardFinancials.series(
        { ...current, heartbeatAt },
        '2026-10-20',
        'endingEquityThb',
        observedNow,
      ).at(-1).value,
      null,
      `${mode} rejects stale or future heartbeat evidence.`,
    );
  }
}
assert.equal(
  DashboardFinancials.series(
    {
      ...stoppedCurrent,
      mode: 'paper',
      autonomy: { funnel: { date: '2026-10-20', netThb: '99999' } },
    },
    '2026-10-20',
    'realizedNetThb',
    observedNow,
  ).at(-1).value,
  5,
  'Paper keeps the retained live report and ignores its simulated funnel.',
);
assert.equal(
  DashboardFinancials.series(
    null,
    '2026-10-20',
    'realizedNetThb',
    observedNow,
  ).at(-1).value,
  null,
  'Missing trading scope remains a gap.',
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
const tradingIssueCases = [
  ['trading-fault', { ...live, fault: 'failed' }],
  ['uncertain-orders', { ...live, live: { orders: [{ status: 'unknown' }] } }],
  ['engine-stale', { ...live, heartbeatAt: null }],
  ['mandate', live],
  ['notification', { ...live, notifications: { unknown: 1 } }],
];
for (const [key, tradingCase] of tradingIssueCases) {
  assert.equal(
    DashboardPresentation.issues(null, tradingCase, activity).find(
      (issue) => issue.key === key,
    ).href,
    '/crypto',
    `${key} routes to Crypto.`,
  );
}
assert.equal(
  DashboardPresentation.issues(
    {
      error: 'Wallet unavailable',
      summary: { totalAssets: null },
      performance: { unreconciledSymbols: [] },
    },
    null,
    activity,
  ).find((issue) => issue.key === 'wallet-error').href,
  '/crypto',
);
assert.equal(
  DashboardPresentation.issues(null, null, {
    ...activity,
    overview: { configured: false, company: null },
  }).find((issue) => issue.key === 'team-auth').href,
  '/daily-brief',
);
assert.equal(
  DashboardPresentation.issues(null, null, {
    ...activity,
    overview: {
      configured: true,
      company: {
        status: 'waitingForPortfolio',
        error: 'Portfolio unavailable',
      },
    },
  }).find((issue) => issue.key === 'team-error').href,
  '/daily-brief',
);
assert.equal(
  DashboardPresentation.issues(null, tradingIssueCases[1][1], activity).some(
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
