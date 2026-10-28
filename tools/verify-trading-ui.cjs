const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');

const transpileTypeScript = (module, filename) => {
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
      jsx: ts.JsxEmit.ReactJSX,
      esModuleInterop: true,
    },
  });
  module._compile(output.outputText, filename);
};
require.extensions['.ts'] = transpileTypeScript;
require.extensions['.tsx'] = transpileTypeScript;
const { NextRequest } = require('next/server');
const {
  TradingController,
} = require('../apps/web/app/api/trading/TradingController.ts');
const {
  TradingService,
} = require('../apps/web/features/trading/services/TradingService.ts');
const {
  tradingOverviewSchema,
} = require('../apps/web/features/trading/contracts/TradingOverview.ts');
const {
  tradingRulesSchema,
} = require('../apps/web/features/trading/contracts/TradingRules.ts');
const {
  RequestService,
} = require('../apps/web/shared/services/RequestService.ts');
const {
  RiskController,
} = require('../apps/web/app/api/crypto/risk/RiskController.ts');
const {
  riskPolicySchema,
} = require('../apps/web/features/crypto/contracts/RiskPolicy.ts');
const {
  default: TradingPanel,
} = require('../apps/web/features/trading/components/trading-panel/TradingPanel.tsx');
const {
  default: TradingControls,
} = require('../apps/web/features/trading/components/trading-controls/TradingControls.tsx');
const {
  default: TradingAutonomyView,
} = require('../apps/web/features/trading/components/trading-autonomy-view/TradingAutonomyView.tsx');
const {
  default: TradingBookView,
} = require('../apps/web/features/trading/components/trading-book-view/TradingBookView.tsx');

function verifyObjectivePresentation() {
  const autonomy = {
    activity: 'Fixture monitoring',
    eligibleMarkets: 1,
    totalMarkets: 1,
    capital: null,
    evaluation: null,
    readiness: null,
    funnel: null,
    opportunityReview: null,
    candidates: [],
  };
  const withoutObjective = renderToStaticMarkup(
    React.createElement(TradingAutonomyView, {
      overview: { ...overview, autonomy, objective: undefined },
    }),
  );
  assert.match(withoutObjective, /Target unavailable/);
  assert.match(
    withoutObjective,
    /This snapshot does not include an account target/,
  );
  assert.doesNotMatch(withoutObjective, /10,000|31 Oct 2026/);

  const withObjective = renderToStaticMarkup(
    React.createElement(TradingAutonomyView, {
      overview: {
        ...overview,
        autonomy,
        objective: {
          id: 'fixture-objective',
          targetThb: 23456,
          deadlineExclusive: '2026-12-11T05:30:00Z',
          includesDeposits: true,
          timeZone: 'Asia/Bangkok',
          observedAt: '2026-10-25T05:00:00Z',
          portfolioValueThb: null,
          remainingThb: null,
          depositsThb: null,
          tradingPnlThb: null,
          valuationStatus: 'unknown',
          reason: 'Fixture valuation unavailable.',
        },
      },
    }),
  );
  assert.match(withObjective, /Portfolio target ฿23,456/);
  assert.match(withObjective, /Before 11\/12\/2026, 12:30:00 Bangkok/);
  assert.match(withObjective, /Includes deposits/);
  assert.match(withObjective, /Unknown/);
  assert.match(withObjective, /Fixture valuation unavailable/);
  assert.match(withObjective, /Valued 25\/10\/2026, 12:00:00 Bangkok/);
}

function verifyTradingBookPresentation() {
  const adaptiveRules = {
    ...rules,
    version: 'adaptive-intraday-v2',
    adaptive: {
      excludedSymbols: [],
      risk: {
        maximumExposureFraction: 0.5,
        maximumConcentrationFraction: 0.5,
        riskToStopFraction: 0.01,
      },
      dailyLossFraction: 0.05,
    },
  };
  const paperBook = {
    cashThb: '700',
    equityThb: '800',
    realizedGrossThb: null,
    realizedNetThb: null,
    feesThb: '0',
    unrealizedThb: null,
    positions: [
      {
        symbol: 'DOGE',
        quantity: '50',
        costThb: '100',
        openedAt: '2026-10-16T03:00:00Z',
        exitPlan: null,
      },
    ],
    orders: [],
  };
  const base = {
    ...overview,
    mode: 'paper',
    rules: adaptiveRules,
    paper: paperBook,
    live: paperBook,
    autonomy: {
      capital: {
        availableThb: '9000',
        equityThb: '9100',
        exposureThb: '100',
      },
      realizedNetTodayThb: '12.5',
    },
  };
  const paperMarkup = renderToStaticMarkup(
    React.createElement(TradingBookView, { overview: base }),
  );
  assert.match(paperMarkup, /Simulated cash/);
  assert.match(paperMarkup, /฿700/);
  assert.match(paperMarkup, /Simulated cost/);
  assert.match(paperMarkup, /Simulated net today/);
  assert.match(paperMarkup, /Equity ฿800/);
  assert.doesNotMatch(paperMarkup, /฿9,000|฿9,100/);
  assert.match(paperMarkup, /Ledger details[^]*?1 lot</);
  const fixedExact = renderToStaticMarkup(
    React.createElement(TradingBookView, {
      overview: {
        ...base,
        mode: 'stopped',
        rules,
        live: {
          ...paperBook,
          cashThb: '1.0049999999999999999999999999',
          positions: [
            { ...paperBook.positions[0], costThb: '9007199254740993' },
            { ...paperBook.positions[0], symbol: 'BTC', costThb: '0.005' },
          ],
        },
      },
    }),
  ).replace(/<style[^>]*>[^]*?<\/style>/g, '');
  assert.match(fixedExact, /Ledger cash<\/dt><dd[^>]*>฿1<\/dd>/);
  assert.match(
    fixedExact,
    /Ledger cost<\/dt><dd[^>]*>฿9,007,199,254,740,993\.01<\/dd>/,
  );
  assert.match(fixedExact, /Ledger details[^]*?2 lots</);
  const emptyBook = renderToStaticMarkup(
    React.createElement(TradingBookView, {
      overview: { ...base, paper: { ...paperBook, positions: [] } },
    }),
  );
  assert.match(emptyBook, /Ledger details[^]*?0 lots</);
  const unknownPaper = renderToStaticMarkup(
    React.createElement(TradingBookView, {
      overview: {
        ...base,
        paper: { ...paperBook, equityThb: null },
        autonomy: { ...base.autonomy, realizedNetTodayThb: null },
      },
    }),
  );
  assert.match(unknownPaper, /Equity Unknown/);
  assert.doesNotMatch(unknownPaper, /฿9,100|฿12.5/);
  const paperSystem = renderToStaticMarkup(
    React.createElement(TradingAutonomyView, {
      overview: {
        ...base,
        autonomy: { ...base.autonomy, candidates: [], nextCheckAt: null },
      },
    }),
  );
  assert.match(paperSystem, /Simulated equity ฿800/);
  assert.doesNotMatch(paperSystem, /฿9,100/);

  for (const status of [
    'submitted',
    'partial',
    'filled',
    'cancelled',
    'rejected',
    'expired',
  ]) {
    for (const cancellationState of ['unknown', 'claimed']) {
      const order = {
        id: `terminal-${status}-${cancellationState}`,
        symbol: 'DOGE',
        side: 'buy',
        amount: '100',
        limitPrice: '2',
        remaining: '0',
        status,
        createdAt: '2026-10-16T03:00:00Z',
        brokerOrderId: 'broker-terminal',
        reason: 'Fixture terminal order',
        cancellationState,
        lastInspectedAt: '2026-10-16T03:59:30Z',
        lastInspectionError: null,
      };
      const markup = renderToStaticMarkup(
        React.createElement(TradingBookView, {
          overview: {
            ...base,
            mode: 'stopped',
            live: { ...paperBook, orders: [order] },
          },
        }),
      );
      assert.match(markup, /Active orders \(1\)/);
      assert.match(markup, /Cancellation unconfirmed/);

      const paperMarkupWithLiveOrder = renderToStaticMarkup(
        React.createElement(TradingBookView, {
          overview: {
            ...base,
            mode: 'paper',
            live: { ...paperBook, orders: [order] },
          },
        }),
      );
      assert.match(
        paperMarkupWithLiveOrder,
        /Live orders needing review \(1\)/,
      );
      assert.match(paperMarkupWithLiveOrder, /Cancellation unconfirmed/);
    }
  }

  const confirmed = renderToStaticMarkup(
    React.createElement(TradingBookView, {
      overview: {
        ...base,
        mode: 'stopped',
        live: {
          ...paperBook,
          orders: [
            {
              id: 'confirmed-terminal',
              symbol: 'DOGE',
              side: 'buy',
              amount: '100',
              limitPrice: '2',
              remaining: '0',
              status: 'filled',
              createdAt: '2026-10-16T03:00:00Z',
              brokerOrderId: 'broker-terminal',
              reason: 'Fixture terminal order',
              cancellationState: null,
              lastInspectedAt: '2026-10-16T03:59:30Z',
              lastInspectionError: null,
            },
          ],
        },
      },
    }),
  );
  assert.match(confirmed, /Active orders \(0\)/);
}

const rules = {
  version: 'ema20-50-breakout-v1',
  symbols: ['BTC'],
  allocatedThb: 1000,
  perOrderThb: 100,
  maxPositions: 2,
  dailyLossThb: 100,
  maxSpreadBps: 50,
  slippageBps: 20,
  maxDataAgeSeconds: 90,
  takeProfitBps: 200,
  stopLossBps: 100,
  trailingStopBps: 100,
  timeExitMinutes: 120,
  cooldownMinutes: 15,
  orderLifetimeMinutes: 15,
  feeBps: 25,
  volumeRatio: 1.5,
};
const book = {
  cashThb: '1000',
  equityThb: null,
  realizedGrossThb: null,
  realizedNetThb: null,
  feesThb: '0',
  unrealizedThb: null,
  positions: [],
  orders: [],
};
const overview = {
  accountKey: 'fixture-owner-account',
  mode: 'paper',
  rules,
  rulesHash: 'fixture-rules-hash',
  mandate: null,
  emergencyStopped: false,
  heartbeatAt: '2026-10-16T04:00:00Z',
  fault: null,
  liveActivationAvailable: true,
  notifications: {
    pending: 0,
    sending: 0,
    sent: 1,
    unknown: 0,
    blocked: 0,
    oldestPendingAt: null,
    state: 'ready',
    dashboardAvailable: true,
  },
  exitStudy: {
    protocol: 'paired-exit-v1',
    status: 'collecting',
    verifiedProfit: false,
    startedAt: null,
    observedAt: '2026-10-16T04:00:00Z',
    liveSamples: 0,
    paperSamples: 0,
    pairedClosed: 0,
    unknown: 0,
    minimumDays: 14,
    minimumClosed: 30,
    hypotheticalNetThb: null,
    baselineNetThb: null,
  },
  realtime: {
    connected: true,
    lastEventAt: '2026-10-16T04:00:00Z',
    watchedSymbols: ['BTC'],
    dirtySymbols: [],
    coalesced: 2,
    dropped: 0,
    decisionP50Ms: 120,
    decisionP95Ms: 1800,
    lastExitEvaluationAt: '2026-10-16T03:59:58Z',
    lastReconciliationAt: '2026-10-16T03:59:30Z',
    lastSubmitLatencyMs: null,
  },
  paper: book,
  live: book,
  reports: [
    {
      date: '2026-10-16',
      mode: 'paper',
      revision: 1,
      createdAt: '2026-10-16T16:59:00Z',
      text: 'รายงานทดสอบ: ผลกำไรยังไม่ทราบ',
      status: 'prepared',
      startingEquityThb: null,
      endingEquityThb: null,
      realizedNetThb: null,
    },
  ],
};

async function verifyActionRecovery() {
  const savedWindow = global.window;
  global.window = {
    setTimeout: () => 1,
    clearTimeout: () => undefined,
  };
  const panel = new TradingPanel({ date: '2026-10-23' });
  panel.mounted = true;
  panel.setState = (next) => {
    const update = typeof next === 'function' ? next(panel.state) : next;
    panel.state = { ...panel.state, ...update };
  };
  panel.state = {
    ...panel.state,
    overview: { ...overview, mode: 'autoLive' },
  };
  let commandCalls = 0;
  let statusCalls = 0;
  let releaseStatus;
  const statusPending = new Promise((resolve) => {
    releaseStatus = resolve;
  });
  const service = {
    get: async () => {
      statusCalls += 1;
      return await statusPending;
    },
  };
  panel.context = service;
  const run = panel.run.bind(panel);
  const receive = panel.receive.bind(panel);
  const rejected = run(async () => {
    commandCalls += 1;
    throw new Error('Fixture activation refused.');
  });
  await Promise.resolve();
  assert.equal(commandCalls, 1);
  assert.equal(statusCalls, 1);
  assert.equal(
    await run(async () => {
      commandCalls += 1;
    }),
    null,
  );
  assert.equal(commandCalls, 1);
  assert.equal(panel.state.error, 'Trading status is being revalidated.');
  assert.equal(panel.state.overview.mode, 'autoLive');
  releaseStatus({ ...overview, mode: 'stopped' });
  await rejected;
  assert.equal(commandCalls, 1);
  assert.equal(panel.state.overview.mode, 'stopped');
  assert.equal(panel.state.actionError, 'Fixture activation refused.');
  assert.equal(panel.state.error, null);
  assert.equal(panel.state.busy, false);
  service.get = async () => {
    statusCalls += 1;
    throw new Error('Fixture status unavailable.');
  };
  await run(async () => {
    commandCalls += 1;
    throw new Error('Uncertain fixture request.');
  });
  assert.equal(statusCalls, 2);
  assert.equal(commandCalls, 2);
  assert.equal(panel.state.error, 'Fixture status unavailable.');
  assert.equal(panel.state.actionError, 'Uncertain fixture request.');
  receive({ ...overview, accountKey: 'other-fixture-account' });
  assert.equal(panel.state.actionError, null);
  panel.setState({ actionError: 'Old policy failure.' });
  receive({ ...panel.state.overview, rulesHash: 'other-rules' });
  assert.equal(panel.state.actionError, null);
  panel.mounted = false;
  panel.actionInFlight = false;
  await run(async () => {
    commandCalls += 1;
    throw new Error('Unmounted command failure.');
  });
  assert.equal(statusCalls, 2);
  global.window = savedWindow;
}

async function verifyEmergencyStopRecovery() {
  const flatten = (node) =>
    Array.isArray(node)
      ? node.flatMap(flatten)
      : node && typeof node === 'object'
        ? [node, ...flatten(node.props?.children)]
        : [];
  const stopped = { ...overview, mode: 'stopped', emergencyStopped: true };
  const panel = new TradingPanel({ date: '2026-10-23' });
  panel.state = { ...panel.state, overview: stopped };
  let disableCalls = 0;
  let unexpectedCalls = 0;
  panel.context = {
    disable: async () => {
      disableCalls += 1;
      return { ...stopped, emergencyStopped: false };
    },
    activate: async () => {
      unexpectedCalls += 1;
    },
    emergencyStop: async () => {
      unexpectedCalls += 1;
    },
  };
  let pending;
  panel.run = (action) => {
    pending = action(new AbortController().signal);
    return pending;
  };
  const controlsProps = flatten(panel.render()).find(
    (node) => node.type === TradingControls,
  ).props;
  const controls = new TradingControls(controlsProps);
  const clear = flatten(controls.render()).find(
    (node) => node.props?.children === 'Clear emergency stop',
  );
  assert(
    clear,
    'Emergency stop must have a visible path back to stopped setup.',
  );
  assert.equal(disableCalls, 0, 'Rendering never clears an emergency stop.');
  clear.props.onClick();
  const result = await pending;
  assert.equal(disableCalls, 1);
  assert.equal(result.mode, 'stopped');
  assert.equal(result.emergencyStopped, false);
  assert.equal(result.mandate, stopped.mandate);
  assert.equal(unexpectedCalls, 0, 'Clearing a stop never activates trading.');
  for (const state of [{ busy: true }, { error: 'Status unavailable' }]) {
    const blocked = new TradingControls({ ...controlsProps, ...state });
    assert.equal(
      flatten(blocked.render()).find(
        (node) => node.props?.children === 'Clear emergency stop',
      ).props.disabled,
      true,
    );
  }
  const liveMandate = {
    id: 'fixture-mandate',
    accountKey: overview.accountKey,
    rulesHash: 'fixture-rules-hash',
    createdAt: '2026-10-16T03:00:00Z',
    expiresAt: '2099-01-01T00:00:00Z',
    revokedAt: null,
  };
  const order = {
    id: 'fixture-order',
    symbol: 'BTC',
    side: 'buy',
    amount: '100',
    limitPrice: '100',
    status: 'unknown',
    createdAt: '2026-10-16T03:00:00Z',
    brokerOrderId: null,
    reason: 'fixture',
    remaining: '100',
  };
  const renderLiveState = (changes = {}) => {
    const panel = new TradingPanel({ date: '2026-10-23' });
    panel.state = {
      ...panel.state,
      overview: {
        ...overview,
        mode: 'autoLive',
        heartbeatAt: new Date().toISOString(),
        mandate: liveMandate,
        live: { ...overview.live, orders: [] },
        ...changes,
      },
    };
    const nodes = flatten(panel.render());
    const chip = nodes.find((node) => node.props?.label);
    const controls = nodes.find((node) => node.type === TradingControls);
    return { chip, controls };
  };
  const healthy = renderLiveState();
  assert.equal(healthy.chip.props.label, 'Auto trading on');
  assert.equal(healthy.chip.props.color, 'success');
  assert.equal(healthy.controls.props.liveOn, true);
  for (const changes of [
    { fault: 'Uncertain broker order — reconciliation required' },
    { live: { ...overview.live, orders: [{ ...order, status: 'unknown' }] } },
    {
      live: { ...overview.live, orders: [{ ...order, status: 'submitting' }] },
    },
    ...['claimed', 'unknown'].map((cancellationState) => ({
      live: {
        ...overview.live,
        orders: [{ ...order, status: 'submitted', cancellationState }],
      },
    })),
  ]) {
    const blocked = renderLiveState(changes);
    assert.equal(blocked.chip.props.label, 'Enabled · Blocked');
    assert.equal(blocked.chip.props.color, 'warning');
    assert.equal(blocked.controls.props.liveOn, true);
  }
  const stale = renderLiveState({
    heartbeatAt: '2026-10-16T03:00:00Z',
    fault: 'feed paused',
  });
  assert.equal(stale.chip.props.label, 'Status unavailable');
  assert.equal(stale.chip.props.color, 'warning');
  const emergency = renderLiveState({
    emergencyStopped: true,
    fault: 'feed paused',
  });
  assert.equal(emergency.chip.props.label, 'Emergency stopped');
  assert.equal(emergency.chip.props.color, 'error');

  const review = renderLiveState({
    mode: 'stopped',
    live: { ...book, orders: [order] },
  });
  assert.equal(review.controls.props.orderReviewNeeded, true);
  const reviewControl = flatten(
    new TradingControls(review.controls.props).render(),
  ).find((node) => node.props?.children === 'Review orders');
  assert.equal(
    reviewControl.props.disabled,
    false,
    'Blocked trading keeps a read-only review action available.',
  );

  const inspectedOrder = {
    ...order,
    cancellationState: 'unknown',
    lastInspectedAt: '2026-10-16T04:00:00Z',
    lastInspectionError: 'No exact broker match found.',
  };
  const parsed = tradingOverviewSchema.parse({
    ...overview,
    live: { ...book, orders: [inspectedOrder] },
  });
  assert.deepEqual(
    parsed.live.orders[0],
    inspectedOrder,
    'Order diagnostics survive transport validation.',
  );
  for (const changes of [
    { lastInspectedAt: 'yesterday' },
    { lastInspectionError: 'x'.repeat(501) },
    { cancellationState: 'invented' },
  ]) {
    assert.equal(
      tradingOverviewSchema.safeParse({
        ...overview,
        live: { ...book, orders: [{ ...order, ...changes }] },
      }).success,
      false,
    );
  }
  assert.equal(
    tradingOverviewSchema.safeParse({
      ...overview,
      live: { ...book, orders: [order] },
    }).success,
    true,
    'Legacy responses without diagnostic fields remain readable.',
  );
}

async function verifyTransport() {
  const evaluationSchema = tradingOverviewSchema.shape.autonomy
    .unwrap()
    .unwrap()
    .shape.evaluation.unwrap()
    .unwrap();
  for (const protocol of [
    'ohlc-scenario-v1',
    'ohlc-scenario-v2',
    'ohlc-scenario-v3',
  ]) {
    const evaluation = {
      protocol,
      verifiedProfit: false,
      observedAt: '2026-10-23T04:00:00Z',
      rulesHash: 'a'.repeat(64),
      warnings: ['Hypothetical current precision assumption.'],
      metrics: [],
    };
    assert.equal(evaluationSchema.parse(evaluation).protocol, protocol);
    assert.throws(() =>
      evaluationSchema.parse({ ...evaluation, verifiedProfit: true }),
    );
    assert.throws(() =>
      evaluationSchema.parse({ ...evaluation, protocol: 'unknown' }),
    );
  }
  const originalFetch = global.fetch;
  const savedEnvironment = {
    token: process.env.QUANTARA_TRADING_CONTROL_TOKEN,
    service: process.env.PORTFOLIO_SERVICE_URL,
  };
  const calls = [];
  let result = overview;
  let failure = null;
  let responseStatus = 200;
  const request = (action, body, headers = {}) =>
    new NextRequest(
      'http://127.0.0.1:3000/api/trading' + (action ? '/' + action : ''),
      {
        method: action ? 'POST' : 'GET',
        headers: {
          host: '127.0.0.1:3000',
          ...(action ? { 'content-type': 'application/json' } : {}),
          ...headers,
        },
        ...(action
          ? { body: typeof body === 'string' ? body : JSON.stringify(body) }
          : {}),
      },
    );
  const act = (action, body, headers) =>
    TradingController.action(request(action, body, headers), {
      params: Promise.resolve({ action }),
    });
  const riskRequest = (action, body, headers = {}) =>
    new NextRequest(
      'http://127.0.0.1:3000/api/crypto/risk' + (action ? '/' + action : ''),
      {
        method: action ? 'POST' : 'GET',
        headers: {
          host: '127.0.0.1:3000',
          ...(action ? { 'content-type': 'application/json' } : {}),
          ...headers,
        },
        ...(action
          ? { body: typeof body === 'string' ? body : JSON.stringify(body) }
          : {}),
      },
    );
  const actRisk = (action, body, headers) => {
    const request = riskRequest(action, body, headers);
    return action === 'refresh'
      ? RiskController.refresh(request)
      : RiskController.configure(request);
  };
  try {
    global.fetch = async (target, init) => {
      calls.push({ target, init });
      if (failure) throw failure;
      return Response.json(result, { status: responseStatus });
    };
    delete process.env.QUANTARA_TRADING_CONTROL_TOKEN;
    process.env.PORTFOLIO_SERVICE_URL =
      'http://portfolio.fixture.invalid:5050/';
    assert.equal((await act('activate', {})).status, 503);
    assert.equal(calls.length, 0, 'missing owner token never forwards');
    process.env.QUANTARA_TRADING_CONTROL_TOKEN = 'isolated-fixture-token';
    for (const headers of [
      { origin: 'https://evil.invalid' },
      { host: 'evil.invalid' },
      { 'sec-fetch-site': 'cross-site' },
    ]) {
      assert.equal((await act('disable', {}, headers)).status, 403);
    }
    assert.equal((await act('approve', {})).status, 404);
    for (const body of [
      {},
      { accountKey: ' ', rulesHash: 'a'.repeat(64) },
      { accountKey: overview.accountKey, rulesHash: 'not-a-policy-hash' },
      { accountKey: overview.accountKey, rulesHash: 'a'.repeat(64), rules },
    ]) {
      assert.equal((await act('prepare', body)).status, 400);
    }
    assert.equal((await act('mode', { mode: 'autoLive' })).status, 400);
    assert.equal((await act('mode', { mode: 'invalid' })).status, 400);
    assert.equal(
      (await act('config', { rules: { ...rules, perOrderThb: 1001 } })).status,
      400,
    );
    assert.equal((await act('disable', { confirmed: true })).status, 400);
    assert.equal(
      (
        await act('activate', {
          accountKey: overview.accountKey,
          rulesHash: overview.rulesHash,
          expiresAt: '2099-10-16T12:00:00Z',
        })
      ).status,
      400,
    );
    assert.equal(
      (
        await act('activate', {
          accountKey: overview.accountKey,
          rulesHash: overview.rulesHash,
          expiresAt: '2020-10-16T12:00:00Z',
          acknowledgement: 'enable-auto-trading',
        })
      ).status,
      400,
    );
    assert.equal((await act('disable', '{broken')).status, 400);
    assert.equal(
      (await act('disable', {}, { 'content-type': 'text/plain' })).status,
      415,
    );
    assert.equal(
      (await act('disable', {}, { 'content-length': '8193' })).status,
      413,
    );
    assert.equal((await act('disable', ' '.repeat(8193))).status, 413);
    assert.equal(
      calls.length,
      0,
      'invalid/hostile/oversized controls never forward',
    );
    assert.equal((await TradingController.get(request())).status, 200);
    assert.equal(
      calls.at(-1).target,
      'http://portfolio.fixture.invalid:5050/api/trading',
    );
    assert.equal(calls.at(-1).init.method, 'GET');
    assert.equal(
      calls.at(-1).init.headers,
      undefined,
      'read request has no authority token',
    );
    const preparation = {
      accountKey: overview.accountKey,
      rulesHash: 'a'.repeat(64),
    };
    assert.equal((await act('prepare', preparation)).status, 200);
    assert.equal(
      calls.at(-1).target,
      'http://portfolio.fixture.invalid:5050/api/trading/prepare',
    );
    assert.deepEqual(JSON.parse(calls.at(-1).init.body), preparation);
    assert.equal(
      calls.at(-1).init.headers['X-Quantara-Trading-Control'],
      'isolated-fixture-token',
    );
    const mandate = {
      accountKey: overview.accountKey,
      rulesHash: overview.rulesHash,
      expiresAt: '2099-10-16T12:00:00Z',
      acknowledgement: 'enable-auto-trading',
    };
    assert.equal((await act('activate', mandate)).status, 200);
    assert.deepEqual(JSON.parse(calls.at(-1).init.body), mandate);
    assert.equal(
      calls.at(-1).init.headers['X-Quantara-Trading-Control'],
      'isolated-fixture-token',
    );
    responseStatus = 409;
    result = { error: 'Fixture rules hash changed.' };
    const conflict = await act('activate', mandate);
    assert.equal(conflict.status, 409);
    assert.deepEqual(await conflict.json(), result);
    responseStatus = 200;
    result = overview;
    failure = new Error('Fixture unavailable');
    assert.equal((await act('disable', {})).status, 503);
    failure = null;
    const requests = new RequestService('/api', global.fetch);
    const service = new TradingService(requests);
    assert.deepEqual(await service.get(), overview);
    const intent = {
      id: 'status-fixture',
      symbol: 'BTC',
      side: 'buy',
      amount: '100',
      limitPrice: '10',
      remaining: '100',
      createdAt: overview.heartbeatAt,
      brokerOrderId: null,
      reason: 'Synthetic retained order',
    };
    for (const status of [
      'pending',
      'submitting',
      'submitted',
      'partial',
      'unknown',
      'filled',
      'cancelled',
      'rejected',
      'expired',
    ]) {
      for (const ledger of ['paper', 'live']) {
        result = {
          ...overview,
          [ledger]: { ...overview[ledger], orders: [{ ...intent, status }] },
        };
        assert.deepEqual(
          await service.get(),
          result,
          `Supported ${ledger} status preserved: ${status}`,
        );
      }
    }
    for (const status of [
      '',
      'UNKNOWN',
      'Filled',
      'partiallyFilled',
      'new-provider-state',
      null,
    ]) {
      for (const ledger of ['paper', 'live']) {
        result = {
          ...overview,
          [ledger]: { ...overview[ledger], orders: [{ ...intent, status }] },
        };
        await assert.rejects(
          service.get(),
          /Trading status could not be verified/,
          `Unsupported ${ledger} status was accepted: ${String(status)}`,
        );
      }
    }
    result = {
      ...overview,
      live: { ...overview.live, orders: [{ ...intent, status: 'UNKNOWN' }] },
    };
    const beforeUnverifiedUpdate = calls.length;
    await assert.rejects(
      service.configure(rules),
      /The action may have completed/,
    );
    assert.equal(
      calls.length,
      beforeUnverifiedUpdate + 1,
      'Unverified acknowledgement never repeats a control command.',
    );
    result = overview;
    const signal = new AbortController().signal;
    assert.deepEqual(
      await service.prepare(
        preparation.accountKey,
        preparation.rulesHash,
        signal,
      ),
      overview,
    );
    assert.equal(calls.at(-1).target, '/api/trading/prepare');
    assert.deepEqual(JSON.parse(calls.at(-1).init.body), preparation);
    assert.equal(calls.at(-1).init.signal, signal);
    await service.activate(
      overview.accountKey,
      overview.rulesHash,
      mandate.expiresAt,
      signal,
    );
    assert.equal(calls.at(-1).target, '/api/trading/activate');
    assert.equal(calls.at(-1).init.signal, signal);
    assert.deepEqual(JSON.parse(calls.at(-1).init.body), mandate);
    await service.configure(rules);
    assert.deepEqual(JSON.parse(calls.at(-1).init.body), { rules });
    for (const invalid of [
      { ...overview, mode: 'auto' },
      { ...overview, heartbeatAt: 'yesterday' },
      { ...overview, paper: { ...book, equityThb: 'invented' } },
      { ...overview, rules: { ...rules, symbols: ['BTC', 'BTC'] } },
    ]) {
      result = invalid;
      await assert.rejects(
        service.get(),
        /Trading status could not be verified/,
      );
    }
    assert.equal(tradingOverviewSchema.safeParse(overview).success, true);
    const adaptive = {
      ...rules,
      version: 'adaptive-intraday-v2',
      symbols: [],
      adaptive: {
        universeVersion: 'bitkub-thb-spot-v1',
        includeFutureDeposits: true,
        excludedSymbols: ['USDT'],
        risk: {
          profile: 'aggressive',
          maximumExposureFraction: 1,
          maximumConcentrationFraction: 1,
          riskToStopFraction: 0.05,
          atrMultiplier: 1,
          maximumSpreadFraction: 0.005,
          estimatedFeeRate: 0.0025,
          maximumDataAgeSeconds: 90,
          maximumSignalAgeSeconds: 600,
        },
        dailyLossFraction: 0.1,
        dailyProfitTargetThb: 500,
      },
    };
    assert.equal(
      tradingRulesSchema.safeParse(adaptive).success,
      true,
      'v2 remains valid without an exit policy',
    );
    const exitPolicy = {
      version: 'adaptive-exit-v1',
      trendReviewBars: 3,
      rangeReviewBars: 4,
      trendMaximumMinutes: 120,
      rangeMaximumMinutes: 240,
      trendMinimumMinutes: 5,
      rangeMinimumMinutes: 10,
      maximumReviews: 8,
      noProgressReviews: 2,
      trendTrailingAtr: 1.5,
      rangeTrailingAtr: 2,
      activationAtr: 0.5,
      minimumProgressAtr: 0.25,
    };
    assert.equal(
      tradingRulesSchema.safeParse({
        ...adaptive,
        version: 'adaptive-intraday-v3',
        exitPolicy,
      }).success,
      true,
      'v3 accepts a valid exit policy',
    );
    assert.equal(
      tradingRulesSchema.safeParse({
        ...adaptive,
        version: 'adaptive-intraday-v3',
      }).success,
      false,
      'v3 requires an exit policy',
    );
    assert.equal(
      tradingRulesSchema.safeParse({
        ...adaptive,
        exitPolicy,
      }).success,
      false,
      'v2 rejects an exit policy',
    );
    assert.equal(
      tradingRulesSchema.safeParse({
        ...adaptive,
        version: 'adaptive-intraday-v3',
        exitPolicy: { ...exitPolicy, trendTrailingAtr: 0 },
      }).success,
      false,
      'exit policy bounds are enforced',
    );
    const position = {
      symbol: 'BTC',
      quantity: '1',
      costThb: '100',
      openedAt: '2026-10-16T03:00:00Z',
      peakBid: '110',
      exitPlan: {
        version: 'adaptive-exit-v1',
        regime: 'trend',
        timeframeSeconds: 300,
        nextReviewAt: '2026-10-16T04:00:00Z',
        hardExitAt: '2026-10-16T06:00:00Z',
        effectiveStop: '99.5',
        trailingActive: true,
        reviewCount: 1,
        revision: 2,
        lastReason: 'No progress yet',
      },
      stopPrice: '99',
      targetPrice: '120',
      trailingDistance: '2',
      trailingActive: true,
      timeExitAt: null,
    };
    assert.equal(
      tradingOverviewSchema.safeParse({
        ...overview,
        notifications: {
          pending: 1,
          sending: 0,
          sent: 2,
          unknown: 1,
          blocked: 2,
          dashboardAvailable: true,
          oldestPendingAt: '2026-10-16T03:00:00Z',
          state: 'needsReview',
        },
        paper: { ...book, positions: [position] },
        exitStudy: {
          protocol: 'paired-exit-v1',
          status: 'collecting',
          verifiedProfit: false,
          startedAt: null,
          observedAt: '2026-10-19T00:00:00Z',
          liveSamples: 1,
          paperSamples: 2,
          pairedClosed: 3,
          unknown: 0,
          minimumDays: 14,
          minimumClosed: 30,
          hypotheticalNetThb: '12.5',
          baselineNetThb: null,
        },
      }).success,
      true,
      'notifications, exit plans and legacy exit fields parse',
    );
    assert.equal(
      tradingOverviewSchema.safeParse({
        ...overview,
        notifications: {
          pending: 0,
          sending: 0,
          sent: 0,
          unknown: 0,
          oldestPendingAt: 'not-a-timestamp',
          state: 'ready',
        },
      }).success,
      false,
      'notification timestamps are validated',
    );
    assert.equal(
      tradingOverviewSchema.safeParse({
        ...overview,
        paper: {
          ...book,
          positions: [
            {
              ...position,
              exitPlan: { ...position.exitPlan, effectiveStop: 'NaN' },
            },
          ],
        },
      }).success,
      false,
      'exit plan decimals are validated',
    );
    assert.equal(
      tradingRulesSchema.safeParse({
        ...rules,
        version: 'unimplemented-strategy',
      }).success,
      false,
    );
    const riskHash =
      '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef';
    const riskPolicy = {
      profile: 'aggressive',
      maximumExposureFraction: 1,
      maximumConcentrationFraction: 1,
      riskToStopFraction: 0.05,
      atrMultiplier: 1,
      maximumSpreadFraction: 0.005,
      estimatedFeeRate: 0.0025,
      maximumDataAgeSeconds: 90,
      maximumSignalAgeSeconds: 600,
    };
    assert.equal((await RiskController.get(riskRequest())).status, 200);
    assert.equal(
      calls.at(-1).target,
      'http://portfolio.fixture.invalid:5050/api/crypto/risk',
    );
    assert.equal((await actRisk('refresh', {})).status, 200);
    assert.equal(
      calls.at(-1).target,
      'http://portfolio.fixture.invalid:5050/api/crypto/risk/refresh',
    );
    assert.equal(
      (
        await actRisk('policy', {
          policy: riskPolicy,
          expectedPolicyHash: riskHash,
        })
      ).status,
      200,
    );
    assert.equal(
      calls.at(-1).target,
      'http://portfolio.fixture.invalid:5050/api/crypto/risk/policy',
    );
    assert.deepEqual(JSON.parse(calls.at(-1).init.body), {
      policy: riskPolicy,
      expectedPolicyHash: riskHash,
    });
    for (const invalid of [
      {
        policy: { ...riskPolicy, maximumExposureFraction: 1.1 },
        expectedPolicyHash: riskHash,
      },
      { policy: riskPolicy, expectedPolicyHash: 'short' },
      {
        policy: { ...riskPolicy, maximumDataAgeSeconds: 9 },
        expectedPolicyHash: riskHash,
      },
    ]) {
      assert.equal((await actRisk('policy', invalid)).status, 400);
    }
    assert.equal(riskPolicySchema.safeParse(riskPolicy).success, true);
    assert.equal(
      riskPolicySchema.safeParse({ ...riskPolicy, estimatedFeeRate: 0.02 })
        .success,
      false,
    );
    for (const filename of [
      'apps/web/features/trading/components/trading-panel/TradingPanel.tsx',
      'apps/web/features/trading/services/TradingService.ts',
    ]) {
      assert(
        !fs
          .readFileSync(filename, 'utf8')
          .includes('QUANTARA_TRADING_CONTROL_TOKEN'),
      );
    }
    console.log(
      'Trading transport: origin/token/body/mode/acknowledgement guards and validated services pass; zero real HTTP requests.',
    );
  } finally {
    global.fetch = originalFetch;
    if (savedEnvironment.token === undefined)
      delete process.env.QUANTARA_TRADING_CONTROL_TOKEN;
    else process.env.QUANTARA_TRADING_CONTROL_TOKEN = savedEnvironment.token;
    if (savedEnvironment.service === undefined)
      delete process.env.PORTFOLIO_SERVICE_URL;
    else process.env.PORTFOLIO_SERVICE_URL = savedEnvironment.service;
  }
}

async function verifyBrowser() {
  const { chromium } = require('playwright');
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
  const errors = [];
  const writes = [];
  let current = structuredClone(overview);
  let unavailable = false;
  let activationFailures = 1;
  page.on('pageerror', (error) => errors.push(error.message));
  // Every API call is intercepted before navigation. This fixture cannot reach an account or broker.
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/team')
      return route.fulfill({
        json: {
          configured: true,
          latestRun: null,
          company: {
            enabled: false,
            status: 'stopped',
            nextRunAt: null,
            lastRunId: null,
            error: null,
          },
        },
      });
    if (path === '/api/team/brief')
      return route.fulfill({
        json: {
          date: new URL(request.url()).searchParams.get('date'),
          runs: [],
        },
      });
    if (path === '/api/crypto/orders')
      return route.fulfill({ json: { proposals: [] } });
    if (path === '/api/trading')
      return route.fulfill(
        unavailable
          ? { status: 503, json: { error: 'Fixture trading unavailable.' } }
          : { json: current },
      );
    if (path.startsWith('/api/trading/') && request.method() === 'POST') {
      const body = request.postDataJSON();
      writes.push({ path, body });
      const preparedRules = {
        ...rules,
        version: 'adaptive-intraday-v2',
        symbols: [],
        maxPositions: 16,
        adaptive: {
          universeVersion: 'bitkub-thb-spot-v1',
          includeFutureDeposits: true,
          excludedSymbols: ['USDT', 'USDC', 'DAI'],
          risk: {
            profile: 'aggressive',
            maximumExposureFraction: 1,
            maximumConcentrationFraction: 1,
            riskToStopFraction: 0.05,
            atrMultiplier: 1,
            maximumSpreadFraction: 0.005,
            estimatedFeeRate: 0.0025,
            maximumDataAgeSeconds: 90,
            maximumSignalAgeSeconds: 600,
          },
          dailyLossFraction: 0.1,
          dailyProfitTargetThb: 500,
        },
      };
      if (path.endsWith('/prepare')) {
        assert.deepEqual(body, {
          accountKey: current.accountKey,
          rulesHash: current.rulesHash,
        });
      }
      const nextRules = path.endsWith('/prepare') ? preparedRules : body.rules;
      if (path.endsWith('/config') || path.endsWith('/prepare'))
        current = {
          ...current,
          mode: 'stopped',
          rules: nextRules,
          rulesHash:
            nextRules.version === 'adaptive-intraday-v1' ||
            nextRules.version === 'adaptive-intraday-v2'
              ? 'adaptive-fixture-hash'
              : 'saved-fixture-hash',
          autonomy: nextRules.adaptive
            ? {
                observedAt: '2026-10-16T04:00:00Z',
                universeAt: '2026-10-16T04:00:00Z',
                totalMarkets: 120,
                eligibleMarkets: 115,
                checkedMarkets: 64,
                activity:
                  'Scanning markets; monitoring an ETH pullback candidate.',
                nextCheckAt: '2026-10-16T04:00:10Z',
                capital: {
                  observedAt: '2026-10-16T04:00:00Z',
                  availableThb: '5000',
                  equityThb: '5000',
                  exposureThb: '0',
                  reservedThb: '0',
                  blocker: null,
                },
                candidates: [
                  {
                    symbol: 'ETH',
                    at: '2026-10-16T04:00:00Z',
                    reason:
                      'Waiting for support; next completed candle required.',
                    strategyVersion: 'pullback-v1',
                  },
                ],
                dailyProfitTargetThb: '500',
                realizedNetTodayThb: '12.5',
                readiness: {
                  readyMarkets: 62,
                  unavailableMarkets: 2,
                  frames: [
                    {
                      symbol: 'ETH',
                      timeframeSeconds: 900,
                      checkedAt: '2026-10-16T04:00:00Z',
                      source: 'bitkub-tradingview',
                      status: 'ready',
                      gapCause: 'none',
                      completedBars: 120,
                      contiguousBars: 120,
                      missingIntervals: 0,
                      verifiedZeroVolumeBars: 0,
                      reason: null,
                    },
                  ],
                },
                funnel: {
                  date: '2026-10-16',
                  coverage: 'complete',
                  signals: 3,
                  submittedOrders: 1,
                  filledOrders: 1,
                  closedTrades: 1,
                  netThb: '12.5',
                  blockedReasons: [
                    { reason: 'No adaptive entry signal', count: 61 },
                  ],
                },
                evaluation: {
                  protocol: 'ohlc-scenario-v3',
                  verifiedProfit: false,
                  observedAt: '2026-10-16T04:00:00Z',
                  rulesHash: 'a'.repeat(64),
                  warnings: [
                    'Hypothetical OHLC scenarios; buy limits round down and sell limits round up to the tick. Data gaps and uncertain fills remain Unknown.',
                  ],
                  metrics: [
                    {
                      symbol: 'ETH',
                      timeframeSeconds: 900,
                      segment: 'validation',
                      strategy: 'breakout',
                      samples: 51,
                      trades: 1,
                      unknownFills: 1,
                      netThb: null,
                      feesThb: 2.5,
                      maxDrawdownThb: null,
                      cashBenchmarkThb: 0,
                      insufficientSamples: true,
                    },
                  ],
                },
                opportunityReview: {
                  protocol: 'observed-book-v1',
                  sampleLimit: 8,
                  assumptions:
                    'Sampled hypothetical books and fees. Separate from actual account profit.',
                  days: [],
                },
              }
            : null,
        };
      else if (path.endsWith('/activate')) {
        if (activationFailures > 0) {
          activationFailures -= 1;
          return route.fulfill({
            status: 503,
            json: { error: 'Fixture activation refused.' },
          });
        }
        current = {
          ...current,
          mode: 'autoLive',
          live: {
            ...current.live,
            cashThb:
              current.autonomy?.capital?.availableThb ?? current.live.cashThb,
          },
          mandate: {
            id: 'fake-mandate',
            accountKey: body.accountKey,
            rulesHash: body.rulesHash,
            expiresAt: body.expiresAt,
            createdAt: '2026-10-16T04:00:00Z',
            revokedAt: null,
          },
        };
      } else if (path.endsWith('/disable'))
        current = {
          ...current,
          mode: 'stopped',
          mandate: current.mandate
            ? { ...current.mandate, revokedAt: '2026-10-16T04:00:00Z' }
            : null,
        };
      else if (path.endsWith('/emergency-stop'))
        current = { ...current, mode: 'stopped', emergencyStopped: true };
      else if (path.endsWith('/mode'))
        current = { ...current, mode: body.mode };
      return route.fulfill({ json: current });
    }
    return route.fulfill({
      status: 404,
      json: { error: 'Unrecognized fixture request.' },
    });
  });
  try {
    await page.clock.install({ time: new Date('2026-10-16T04:00:00Z') });
    const collapseTeamChat = async () => {
      await page
        .getByRole('button', {
          name:
            page.viewportSize().width >= 1024
              ? 'Collapse team chat'
              : 'Expand team chat',
          exact: true,
        })
        .waitFor();
      const collapse = page.getByRole('button', {
        name: 'Collapse team chat',
        exact: true,
      });
      if (await collapse.isVisible()) {
        await collapse.click();
        await page.clock.runFor(300);
      }
    };
    await page.goto(
      (process.env.TEST_URL || 'http://127.0.0.1:3000') + '/crypto',
    );
    await collapseTeamChat();
    await page.getByText('Paper trading', { exact: true }).waitFor();
    await page
      .locator('summary')
      .filter({ hasText: 'Orders & positions' })
      .click();
    await page.locator('summary').filter({ hasText: 'System details' }).click();
    await page.getByText('Connected · 1 watched', { exact: true }).waitFor();
    await page
      .getByText('Notifications: 0 pending · Job history', { exact: true })
      .waitFor();
    const jobHistory = page.getByRole('link', {
      name: 'Job history',
      exact: true,
    });
    assert.equal(await jobHistory.getAttribute('href'), '/api/trading/jobs');
    assert.equal(await jobHistory.getAttribute('target'), '_blank');
    await page
      .getByText('Adaptive exit study · Research only', { exact: true })
      .waitFor();
    assert.equal(
      await page.getByText('0 / 30 live pairs', { exact: false }).count(),
      1,
      'exit study starts with no prospective samples',
    );
    await page
      .locator('summary')
      .filter({ hasText: 'Adaptive exit study' })
      .click();
    await page
      .getByText('Waiting for prospective entries', { exact: false })
      .waitFor();
    await page
      .locator('summary')
      .filter({ hasText: 'Adaptive exit study' })
      .click();
    await page.getByText('Feed diagnostics', { exact: true }).click();
    await page
      .getByText('Received → decision p50/p95: 120 ms / 1,800 ms', {
        exact: true,
      })
      .waitFor();
    await page
      .getByText('Decision → submit: Unknown', { exact: true })
      .waitFor();
    assert.equal(writes.length, 0, 'mount has no control POST');
    assert.equal(
      await page.getByText('รายงานทดสอบ: ผลกำไรยังไม่ทราบ').isVisible(),
      false,
      'reports are collapsed',
    );
    assert.equal(
      await page
        .getByLabel('Allocated capital (THB)', { exact: false })
        .isVisible(),
      false,
      'settings are off the main page',
    );
    const summary = page
      .locator('summary')
      .filter({ hasText: 'Daily reports' });
    await summary.click();
    await page.getByText('รายงานทดสอบ: ผลกำไรยังไม่ทราบ').waitFor();
    await page
      .getByRole('button', { name: 'Trading settings', exact: true })
      .click();
    assert.equal(await page.locator('form input[type=number]').count(), 15);
    assert.equal(
      await page.getByLabel('Take profit (bps)', { exact: false }).isVisible(),
      false,
      'technical fields are advanced',
    );
    await page
      .getByLabel('Allocated capital (THB)', { exact: false })
      .fill('2000');
    await page.getByRole('button', { name: 'Close trading settings' }).click();
    assert.equal(
      await page
        .getByRole('button', { name: 'Set up and review', exact: true })
        .isDisabled(),
      true,
    );
    await page
      .getByRole('button', { name: 'Trading settings', exact: true })
      .click();
    await page
      .getByRole('button', { name: 'Save rules and disarm live' })
      .click();
    await page.waitForFunction(
      () => document.querySelector('button[type=submit]')?.disabled === true,
    );
    await page.getByRole('button', { name: 'Close trading settings' }).click();
    await page
      .getByRole('button', { name: 'Set up and review', exact: true })
      .waitFor({ state: 'visible' });
    await page.waitForFunction(
      () =>
        document.querySelector('input[name=allocatedThb]')?.value === '2000',
    );
    assert.equal(writes.length, 1);
    assert.deepEqual(
      Object.keys(writes[0].body.rules).sort(),
      Object.keys(rules).sort(),
    );
    assert.equal(writes[0].body.rules.allocatedThb, 2000);
    await page
      .getByRole('button', { name: 'Set up and review', exact: true })
      .click();
    await page
      .getByText('Scope: all eligible Bitkub THB spot markets', {
        exact: false,
      })
      .waitFor();
    assert.equal(
      writes.filter((write) => write.path.endsWith('/config')).length,
      1,
    );
    assert.equal(
      writes.filter((write) => write.path.endsWith('/prepare')).length,
      1,
    );
    assert.equal(
      writes.filter((write) => write.path.endsWith('/activate')).length,
      0,
      'setup never activates',
    );
    assert.deepEqual(Object.keys(writes.at(-1).body).sort(), [
      'accountKey',
      'rulesHash',
    ]);
    assert.deepEqual(current.rules.symbols, []);
    const enable = page.getByRole('button', {
      name: 'Enable Auto Trading',
      exact: true,
    });
    assert.equal(await enable.isDisabled(), true, 'owner must choose expiry');
    await page.getByText('Choose an expiry to enable auto trading.').waitFor();
    await page
      .getByLabel('Mandate expiry (your local time)')
      .fill('2026-10-15T12:00');
    assert.equal(await enable.isDisabled(), true, 'past expiry cannot enable');
    await page.getByText('Choose a future date and time.').waitFor();
    await page
      .getByLabel('Mandate expiry (your local time)')
      .fill('2026-12-17T12:00');
    assert.equal(
      await enable.isDisabled(),
      true,
      'expiry beyond 30 days cannot enable',
    );
    await page.getByText('Expiry must be within the next 30 days.').waitFor();
    await page.getByRole('button', { name: '7 days', exact: true }).click();
    assert.equal(await enable.isDisabled(), false);
    assert.equal(writes.length, 2, 'choosing expiry never activates');
    await page
      .getByLabel('Mandate expiry (your local time)')
      .fill('2026-10-17T12:00');
    assert.equal(await enable.isDisabled(), false);
    await enable.click();
    await page.getByText('Fixture activation refused.').first().waitFor();
    await page.getByText('Auto trading off', { exact: true }).waitFor();
    assert.equal(
      await page.getByText('Status unavailable', { exact: true }).count(),
      0,
      'rejected command revalidates status without waiting for the ten-second poll',
    );
    assert.equal(
      await page.getByText('Fixture activation refused.').first().isVisible(),
      true,
      'control failure remains visible after a successful status poll',
    );
    assert.equal(
      writes.filter((write) => write.path.endsWith('/activate')).length,
      1,
      'status polling never retries a refused activation',
    );
    assert.equal(
      await page.getByText('Auto trading off', { exact: true }).count(),
      1,
      'refused activation leaves auto trading stopped',
    );
    await page
      .getByRole('button', { name: 'Review & enable', exact: true })
      .click();
    await page.getByRole('button', { name: '7 days', exact: true }).click();
    await page
      .getByRole('button', { name: 'Enable Auto Trading', exact: true })
      .click();
    await page.getByText('Auto trading on', { exact: true }).waitFor();
    assert.equal(
      writes.filter((write) => write.path.endsWith('/activate')).length,
      2,
    );
    assert.equal(
      await page.getByText('Fixture activation refused.').count(),
      0,
    );
    assert.deepEqual(writes.at(-1).body, {
      accountKey: current.accountKey,
      rulesHash: 'adaptive-fixture-hash',
      expiresAt: writes.at(-1).body.expiresAt,
      acknowledgement: 'enable-auto-trading',
    });
    current = { ...current, heartbeatAt: '2026-10-16T03:00:00Z' };
    await page.reload();
    await page.getByText('Live status is unknown.', { exact: false }).waitFor();
    await collapseTeamChat();
    assert.equal(
      writes.length,
      4,
      'stale worker status never grants or revokes authority',
    );
    current = { ...current, heartbeatAt: '2026-10-16T04:00:00Z' };
    await page.getByRole('button', { name: 'Disable', exact: true }).click();
    await page.getByText('Auto trading off', { exact: true }).waitFor();
    assert.equal(writes.at(-1).path, '/api/trading/disable');
    fs.mkdirSync('.impeccable/review', { recursive: true });
    for (const [device, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 820, 1180],
      ['phone', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      const panel = page.locator('#trading > .MuiPaper-root').first();
      const settingsButton = panel.getByRole('button', {
        name: 'Trading settings',
        exact: true,
      });
      assert.equal(await settingsButton.innerText(), 'Settings');
      for (const name of [
        'Orders & positions',
        'System details',
        'Daily reports',
      ]) {
        assert.equal(
          await panel
            .locator('summary')
            .filter({ hasText: name })
            .evaluate((element) => element.parentElement.open),
          false,
        );
      }
      for (const name of [
        'Review & enable',
        'Trading settings',
        'Emergency stop',
      ]) {
        const button = panel.getByRole('button', { name, exact: true });
        assert.equal(await button.isVisible(), true);
        const bounds = await button.boundingBox();
        assert(
          bounds &&
            bounds.height >= 44 &&
            bounds.x >= 0 &&
            bounds.x + bounds.width <= width,
          device + ': visible, reachable control',
        );
      }
      await panel.screenshot({
        path: '.impeccable/review/trading-simple-' + device + '.png',
        animations: 'disabled',
      });
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await summary.click();
    const chooseReportDay = async (day) => {
      await page.getByRole('button', { name: /choose date/i }).click();
      await page.clock.runFor(300);
      await page.getByRole('gridcell', { name: day, exact: true }).click();
      const ok = page.getByRole('button', { name: 'OK', exact: true });
      if (await ok.isVisible()) await ok.click();
    };
    await chooseReportDay('15');
    await page.getByText('No trading report for this date.').waitFor();
    await chooseReportDay('16');
    await page.getByText('รายงานทดสอบ: ผลกำไรยังไม่ทราบ').waitFor();
    await summary.click();
    fs.mkdirSync('.impeccable/review', { recursive: true });
    await page.locator('summary').filter({ hasText: 'System details' }).click();
    await page
      .getByText('Last scan: 62 ready · 2 unavailable', { exact: true })
      .waitFor();
    await page.getByText('Today: 3 signals', { exact: false }).waitFor();
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 768, 1024],
      ['phone', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.waitForTimeout(150);
      await page.screenshot({
        path: '.impeccable/review/trading-' + name + '.png',
        fullPage: true,
        animations: 'disabled',
      });
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth + 1,
        ),
        name + ' has no horizontal overflow',
      );
      const primaryAction = page
        .locator('#trading')
        .getByRole('button', { name: 'Review & enable', exact: true });
      const bounds = await primaryAction.boundingBox();
      assert(
        bounds && bounds.y + bounds.height <= height,
        name + ' primary action is in the first viewport',
      );
      const writesBeforeSettings = writes.length;
      await page
        .getByRole('button', { name: 'Trading settings', exact: true })
        .click();
      const settings = page.getByRole('dialog', {
        name: 'Trading settings',
        exact: true,
      });
      await settings
        .getByText('System-managed trading plan', { exact: true })
        .waitFor();
      assert.equal(await settings.locator('input:visible').count(), 0);
      assert.equal(
        await settings
          .getByRole('button', { name: 'Save overrides and disarm live' })
          .isVisible(),
        false,
      );
      assert.equal(
        await settings
          .getByRole('button', { name: 'Paper', exact: true })
          .isVisible(),
        false,
      );
      await settings
        .getByText('Saved maximum holding time: 120 minutes.', { exact: true })
        .waitFor();
      assert.equal(
        writes.length,
        writesBeforeSettings,
        'Opening settings never changes rules or authority',
      );
      await page.screenshot({
        path: `.impeccable/review/automatic-settings-${name}.png`,
        fullPage: false,
        animations: 'disabled',
      });
      assert(
        await settings.evaluate(
          (element) => element.scrollWidth <= element.clientWidth + 1,
        ),
      );
      const overridesBounds = await settings
        .locator('summary')
        .filter({ hasText: 'Advanced overrides' })
        .boundingBox();
      assert(
        overridesBounds && overridesBounds.y + overridesBounds.height <= height,
        `${name} settings fit without opening numeric fields`,
      );
      await settings
        .getByRole('button', { name: 'Close trading settings' })
        .click();
    }
    await page
      .getByRole('button', { name: 'Trading settings', exact: true })
      .click();
    assert.equal(
      await page.getByLabel('Allowed symbols', { exact: false }).count(),
      0,
      'adaptive mode requires no fixed symbols',
    );
    await page
      .locator('summary')
      .filter({ hasText: 'Advanced overrides' })
      .click();
    await page.getByLabel('Maximum exposure (%)', { exact: false }).waitFor();
    await page.screenshot({
      path: '.impeccable/review/trading-settings-phone.png',
      fullPage: true,
      animations: 'disabled',
    });
    await page.getByRole('button', { name: 'Close trading settings' }).click();
    await page.getByRole('button', { name: 'Review & enable' }).click();
    await page.getByRole('button', { name: '7 days', exact: true }).click();
    await page.screenshot({
      path: '.impeccable/review/trading-review-phone.png',
      fullPage: true,
      animations: 'disabled',
    });
    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    const historical = page
      .locator('summary')
      .filter({ hasText: 'Historical study' });
    await historical.click();
    await page.getByText('Insufficient samples', { exact: false }).waitFor();
    assert(
      await page
        .getByText('buy limits round down and sell limits round up', {
          exact: false,
        })
        .isVisible(),
      'The deployed reader displays v3 study assumptions.',
    );
    assert(
      (await page.getByText('Drawdown Unknown', { exact: false }).count()) > 0,
    );
    assert(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth + 1,
      ),
      'Expanded study fits the phone',
    );
    await page.screenshot({
      path: '.impeccable/review/trading-study-phone.png',
      fullPage: true,
    });
    await historical.click();
    unavailable = true;
    await page.reload();
    await page
      .locator('.MuiPaper-root')
      .filter({
        has: page.getByRole('heading', { name: 'Trading', exact: true }),
      })
      .getByText('Fixture trading unavailable.', { exact: true })
      .waitFor();
    assert.equal(
      await page
        .getByRole('button', { name: 'Enable Auto Trading', exact: true })
        .count(),
      0,
    );
    assert.equal(
      writes.length,
      5,
      'poll/reload/date changes never control trading',
    );
    unavailable = false;
    current = {
      ...current,
      mode: 'paper',
      fault: 'Fixture stale feed: entries paused.',
    };
    await page
      .locator('#trading')
      .getByRole('button', { name: 'Refresh status', exact: true })
      .click();
    await page.getByText('Fixture stale feed: entries paused.').waitFor();
    current = {
      ...current,
      fault: null,
      exitStudy: { ...current.exitStudy, paperSamples: 3 },
      paper: {
        ...current.paper,
        positions: [
          {
            symbol: 'BTC',
            quantity: '1',
            costThb: '100',
            peakBid: '110',
            openedAt: '2026-10-16T03:00:00Z',
            exitPlan: {
              version: 'adaptive-exit-v1',
              regime: 'trend',
              timeframeSeconds: 300,
              nextReviewAt: '2026-10-16T04:20:00Z',
              hardExitAt: '2026-10-16T11:00:00Z',
              effectiveStop: '99.5',
              trailingActive: true,
              reviewCount: 1,
              revision: 2,
              lastReason: 'Setup intact; position retained after review',
            },
          },
        ],
      },
    };
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['phone', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.reload();
      await collapseTeamChat();
      await page
        .locator('summary')
        .filter({ hasText: 'Orders & positions' })
        .click();
      await page
        .locator('summary')
        .filter({ hasText: 'Ledger details' })
        .click();
      await page
        .getByText('Setup intact; position retained after review', {
          exact: false,
        })
        .waitFor();
      assert(
        await page
          .getByText('Maximum hold until', { exact: false })
          .isVisible(),
      );
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth + 1,
        ),
      );
      await page.screenshot({
        path: `.impeccable/review/trading-exit-plan-${name}.png`,
        fullPage: true,
        animations: 'disabled',
      });
    }
    assert.equal(
      writes.length,
      5,
      'Position-plan inspection never changes authority.',
    );
    const unresolvedOrder = {
      id: 'fixture-unconfirmed-order',
      symbol: 'DOGE',
      side: 'buy',
      amount: '100.125',
      limitPrice: '2.75',
      remaining: '100.125',
      status: 'unknown',
      createdAt: '2026-10-16T03:00:00Z',
      brokerOrderId: null,
      reason: 'Fixture strategy signal',
      cancellationState: null,
      lastInspectedAt: '2026-10-16T03:59:30Z',
      lastInspectionError:
        'No exact broker match found. Outcome remains unknown; no order was resent.',
    };
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['phone', 390, 844],
    ]) {
      current = {
        ...current,
        mode: 'stopped',
        fault: 'Uncertain broker order — reconciliation required',
        live: { ...book, orders: [unresolvedOrder] },
        mandate: null,
      };
      await page.setViewportSize({ width, height });
      await page.reload();
      await collapseTeamChat();
      await page
        .getByRole('heading', { name: 'Active orders (1)', exact: true })
        .waitFor();
      assert(
        await page
          .getByText('BUY DOGE · Outcome unknown', { exact: true })
          .isVisible(),
      );
      assert(
        await page
          .getByText(
            'Amount 100.125 THB · Remaining 100.125 THB · Limit 2.75 THB',
            { exact: true },
          )
          .isVisible(),
      );
      assert(
        await page
          .getByText('No exact broker match found.', { exact: false })
          .isVisible(),
      );
      assert.equal(
        await page
          .getByText('Uncertain broker order — reconciliation required', {
            exact: true,
          })
          .count(),
        0,
        'One contextual warning replaces the generic duplicate.',
      );
      assert.equal(
        await page
          .getByRole('button', { name: 'Review & enable', exact: true })
          .count(),
        0,
      );
      await page
        .getByRole('button', { name: 'Review orders', exact: true })
        .click();
      assert.equal(
        await page.evaluate(() => document.activeElement?.textContent),
        'Orders & positions',
      );
      assert.equal(
        await page
          .getByText('Request ID fixture-unconfirmed-order', { exact: false })
          .isVisible(),
        false,
      );
      await page
        .locator('summary')
        .filter({ hasText: /^Order details$/ })
        .press('Enter');
      assert(
        await page
          .getByText(
            'Request ID fixture-unconfirmed-order · Broker ID Unavailable',
            { exact: true },
          )
          .isVisible(),
      );
      assert(
        await page
          .getByText('Strategy reason: Fixture strategy signal', {
            exact: true,
          })
          .isVisible(),
      );
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth + 1,
        ),
      );
      await page.screenshot({
        path: `.impeccable/review/trading-order-review-${name}.png`,
        fullPage: true,
        animations: 'disabled',
      });
    }
    current = {
      ...current,
      mode: 'autoLive',
      fault: null,
      heartbeatAt: '2026-10-16T04:00:00Z',
      mandate: {
        id: 'fixture-live',
        accountKey: current.accountKey,
        rulesHash: current.rulesHash,
        createdAt: '2026-10-16T03:00:00Z',
        expiresAt: '2099-01-01T00:00:00Z',
        revokedAt: null,
      },
      live: {
        ...book,
        orders: [
          {
            ...unresolvedOrder,
            status: 'submitted',
            cancellationState: 'unknown',
            lastInspectionError: null,
          },
        ],
      },
    };
    await page.reload();
    await collapseTeamChat();
    await page.getByText('Enabled · Blocked', { exact: true }).waitFor();
    assert(
      await page
        .getByText('BUY DOGE · Cancellation unconfirmed', { exact: true })
        .isVisible(),
    );
    assert(
      await page
        .getByRole('button', { name: 'Disable', exact: true })
        .isEnabled(),
    );
    current = {
      ...current,
      mode: 'paper',
      live: {
        ...book,
        orders: [
          {
            ...unresolvedOrder,
            side: 'sell',
            amount: '0.00000123',
            remaining: '0.00000123',
            lastInspectedAt: undefined,
            lastInspectionError: undefined,
          },
        ],
      },
    };
    await page.reload();
    await collapseTeamChat();
    await page
      .getByRole('heading', {
        name: 'Live orders needing review (1)',
        exact: true,
      })
      .waitFor();
    assert(
      await page
        .getByText(
          'Amount 0.00000123 DOGE · Remaining 0.00000123 DOGE · Limit 2.75 THB',
          { exact: true },
        )
        .isVisible(),
    );
    assert(
      await page
        .getByText('No check time recorded', { exact: true })
        .isVisible(),
    );
    assert.equal(
      writes.length,
      5,
      'Unknown-order inspection and refresh never change authority or resubmit an order.',
    );
    const objective = {
      id: 'fixture-objective',
      targetThb: 23456,
      deadlineExclusive: '2026-12-11T05:30:00Z',
      includesDeposits: true,
      timeZone: 'Asia/Bangkok',
      observedAt: null,
      portfolioValueThb: null,
      remainingThb: null,
      depositsThb: null,
      tradingPnlThb: null,
      valuationStatus: 'unknown',
      reason: 'Fixture valuation unavailable.',
    };
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['phone', 390, 844],
    ]) {
      current = {
        ...current,
        objective,
        paper: { ...book, cashThb: '700', equityThb: '800' },
        live: {
          ...book,
          orders: [
            {
              ...unresolvedOrder,
              status: 'filled',
              cancellationState: 'unknown',
            },
          ],
        },
        autonomy: {
          ...current.autonomy,
          capital: {
            ...current.autonomy.capital,
            availableThb: '9000',
            equityThb: '9100',
          },
        },
      };
      await page.setViewportSize({ width, height });
      await page.reload();
      await collapseTeamChat();
      await page
        .getByRole('heading', {
          name: 'Live orders needing review (1)',
          exact: true,
        })
        .waitFor();
      assert(
        await page
          .getByText('BUY DOGE · Cancellation unconfirmed', { exact: true })
          .isVisible(),
      );
      const balances = page.getByRole('definition').filter({ hasText: '฿700' });
      assert(
        await page.getByText('Simulated cash', { exact: true }).isVisible(),
      );
      assert.equal(await balances.count(), 1);
      await page
        .locator('summary')
        .filter({ hasText: /^System details$/ })
        .click();
      assert(
        await page
          .getByText('Portfolio target ฿23,456', { exact: true })
          .isVisible(),
      );
      assert(
        await page
          .getByText('Before 11/12/2026, 12:30:00 Bangkok', { exact: false })
          .isVisible(),
      );
      assert(
        await page
          .getByText('Simulated equity ฿800', { exact: true })
          .isVisible(),
      );
      assert.equal(await page.getByText('฿9,100', { exact: false }).count(), 0);
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth + 1,
        ),
      );
      await page.screenshot({
        path: `.impeccable/review/trading-paper-objective-${name}.png`,
        fullPage: true,
        animations: 'disabled',
      });
      current = { ...current, objective: undefined };
      await page.reload();
      await collapseTeamChat();
      await page
        .locator('summary')
        .filter({ hasText: /^System details$/ })
        .click();
      assert(
        await page.getByText('Target unavailable', { exact: true }).isVisible(),
      );
      assert(
        await page
          .getByText('This snapshot does not include an account target.', {
            exact: true,
          })
          .isVisible(),
      );
      await page.screenshot({
        path: `.impeccable/review/trading-objective-unavailable-${name}.png`,
        fullPage: true,
        animations: 'disabled',
      });
    }
    assert.equal(
      writes.length,
      5,
      'Presentation checks never submit commands.',
    );
    assert.deepEqual(errors, []);
    console.log(
      'Trading browser: compact status, advanced settings, exact mandate/expiry validation, one fake activation, selected-date report, failure recovery and desktop/tablet/phone passed; every API call intercepted.',
    );
  } finally {
    await browser.close();
  }
}

Promise.resolve()
  .then(() => verifyObjectivePresentation())
  .then(() => verifyTradingBookPresentation())
  .then(() => verifyActionRecovery())
  .then(() => verifyEmergencyStopRecovery())
  .then(() => verifyTransport())
  .then(async () => {
    if (process.argv.includes('--browser')) await verifyBrowser();
  })
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
