const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');

require.extensions['.ts'] = (module, filename) => {
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  });
  module._compile(output.outputText, filename);
};
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

async function verifyTransport() {
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
    const signal = new AbortController().signal;
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
      await assert.rejects(service.get(), /Trading status response is invalid/);
    }
    assert.equal(tradingOverviewSchema.safeParse(overview).success, true);
    assert.equal(
      tradingRulesSchema.safeParse({
        ...rules,
        version: 'unimplemented-strategy',
      }).success,
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
      if (path.endsWith('/config'))
        current = {
          ...current,
          mode: 'paper',
          rules: body.rules,
          rulesHash: 'saved-fixture-hash',
        };
      else if (path.endsWith('/activate'))
        current = {
          ...current,
          mode: 'autoLive',
          mandate: {
            id: 'fake-mandate',
            accountKey: body.accountKey,
            rulesHash: body.rulesHash,
            expiresAt: body.expiresAt,
            createdAt: '2026-10-16T04:00:00Z',
            revokedAt: null,
          },
        };
      else if (path.endsWith('/disable'))
        current = { ...current, mode: 'stopped' };
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
    await page.goto(
      (process.env.TEST_URL || 'http://127.0.0.1:3000') + '/daily-brief',
    );
    await page.getByText('AutoLive OFF.', { exact: false }).waitFor();
    assert.equal(writes.length, 0, 'mount has no control POST');
    await page.getByText('รายงานทดสอบ: ผลกำไรยังไม่ทราบ').waitFor();
    await page
      .getByRole('button', { name: 'Configure rules and limits' })
      .click();
    assert.equal(await page.locator('form input[type=number]').count(), 15);
    await page
      .getByLabel('Allocated capital (THB)', { exact: false })
      .fill('2000');
    assert.equal(
      await page
        .getByRole('button', { name: 'Review live mandate' })
        .isDisabled(),
      true,
    );
    await page
      .getByRole('button', { name: 'Save rules and disarm live' })
      .click();
    await page
      .getByRole('button', { name: 'Review live mandate' })
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
    await page.getByRole('button', { name: 'Review live mandate' }).click();
    const enable = page.getByRole('button', {
      name: 'Enable Auto Trading',
      exact: true,
    });
    assert.equal(await enable.isDisabled(), true, 'owner must choose expiry');
    await page
      .getByLabel('Mandate expiry (your local time)')
      .fill('2026-10-17T12:00');
    assert.equal(await enable.isDisabled(), false);
    await enable.evaluate((button) => {
      button.click();
      button.click();
    });
    await page.getByText('AutoLive ON.', { exact: false }).waitFor();
    assert.equal(writes.length, 2, 'double activation click sends one request');
    assert.deepEqual(writes[1].body, {
      accountKey: current.accountKey,
      rulesHash: 'saved-fixture-hash',
      expiresAt: writes[1].body.expiresAt,
      acknowledgement: 'enable-auto-trading',
    });
    current = { ...current, heartbeatAt: '2026-10-16T03:00:00Z' };
    await page.reload();
    await page.getByText('Live status is unknown.', { exact: false }).waitFor();
    assert.equal(
      writes.length,
      2,
      'stale worker status never grants or revokes authority',
    );
    current = { ...current, heartbeatAt: '2026-10-16T04:00:00Z' };
    await page.getByRole('button', { name: 'Disable', exact: true }).click();
    await page.getByText('AutoLive OFF.', { exact: false }).waitFor();
    assert.equal(writes.at(-1).path, '/api/trading/disable');
    await page.getByLabel('Date', { exact: true }).fill('2026-10-15');
    await page.getByText('No trading report for this date.').waitFor();
    await page.getByLabel('Date', { exact: true }).fill('2026-10-16');
    await page.getByText('รายงานทดสอบ: ผลกำไรยังไม่ทราบ').waitFor();
    const hideConfiguration = page.getByRole('button', {
      name: 'Hide configuration',
    });
    if (await hideConfiguration.isVisible()) await hideConfiguration.click();
    await page.getByRole('button', { name: 'Review live mandate' }).click();
    await page
      .getByLabel('Mandate expiry (your local time)')
      .fill('2026-10-17T12:00');
    fs.mkdirSync('.impeccable/review', { recursive: true });
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
    }
    unavailable = true;
    await page.reload();
    await page.getByText('Fixture trading unavailable.').waitFor();
    assert.equal(
      await page
        .getByRole('button', { name: 'Enable Auto Trading', exact: true })
        .count(),
      0,
    );
    assert.equal(
      writes.length,
      3,
      'poll/reload/date changes never control trading',
    );
    unavailable = false;
    current = {
      ...current,
      mode: 'paper',
      fault: 'Fixture stale feed: entries paused.',
    };
    await page.getByRole('button', { name: 'Retry', exact: true }).click();
    await page.getByText('Fixture stale feed: entries paused.').waitFor();
    assert.deepEqual(errors, []);
    console.log(
      'Trading browser: editable rules, exact mandate/expiry, one fake activation, selected-date Thai report, failure recovery and desktop/tablet/phone passed; every API call intercepted.',
    );
  } finally {
    await browser.close();
  }
}

verifyTransport()
  .then(async () => {
    if (process.argv.includes('--browser')) await verifyBrowser();
  })
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
