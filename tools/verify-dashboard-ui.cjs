const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verify() {
  const url = process.env.TEST_URL;
  assert(
    url && /^http:\/\/127\.0\.0\.1:(3107|3108)$/.test(url),
    'Use isolated fixture server.',
  );
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    hasTouch: true,
  });
  const writes = [],
    errors = [],
    dates = [];
  const at = '2026-10-20T16:30:00Z',
    date = '2026-10-20';
  const run = {
    id: '218d734e-0446-492d-8b62-ffbd106d044e',
    accountKey: 'fixture-account-a',
    objective: 'Synthetic Dashboard evidence',
    status: 'completed',
    createdAt: at,
    updatedAt: at,
    error: null,
    steps: ['ceo', 'analyst', 'reviewer'].map((role) => ({
      role,
      status: 'completed',
      output: `${role} synthetic research summary.`,
      error: null,
    })),
  };
  let portfolio = {
    positions: [],
    summary: { totalAssets: 12500, availableThb: 2500, holdingCount: 2 },
    updatedAt: at,
    priceUpdatedAt: at,
    historyUpdatedAt: at,
    historyCoverage: 'complete',
    syncing: false,
    completedSymbols: 2,
    totalSymbols: 2,
    error: null,
  };
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
  let trading = {
    accountKey: 'fixture-account-a',
    mode: 'paper',
    rules,
    rulesHash: 'fixture-rules',
    mandate: null,
    emergencyStopped: false,
    heartbeatAt: at,
    fault: null,
    liveActivationAvailable: false,
    live: book,
    paper: book,
    reports: [
      {
        date,
        mode: 'autoLive',
        revision: 1,
        createdAt: at,
        text: 'Synthetic report',
        status: 'local',
        startingEquityThb: null,
        endingEquityThb: null,
        realizedNetThb: '12.25',
      },
      {
        date,
        mode: 'paper',
        revision: 2,
        createdAt: at,
        text: 'Paper must not replace real result.',
        status: 'local',
        startingEquityThb: null,
        endingEquityThb: null,
        realizedNetThb: '99999',
      },
    ],
  };
  page.on('request', (request) => {
    if (request.url().includes('/api/') && request.method() !== 'GET')
      writes.push(request.method());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  await page.route('**/api/**', (route) =>
    route.fulfill({ status: 404, json: { error: 'Unmocked API' } }),
  );
  await page.route('**/api/team', (route) =>
    route.fulfill({
      json: {
        configured: true,
        latestRun: run,
        company: {
          enabled: true,
          status: 'waiting',
          nextRunAt: null,
          lastRunId: run.id,
          error: null,
        },
      },
    }),
  );
  await page.route('**/api/team/brief?*', (route) => {
    const selected = new URL(route.request().url()).searchParams.get('date');
    dates.push(selected);
    return route.fulfill({
      json: { date: selected, runs: selected === date ? [run] : [] },
    });
  });
  await page.route('**/api/crypto/portfolio', (route) =>
    route.fulfill({ json: portfolio }),
  );
  await page.route('**/api/trading', (route) =>
    route.fulfill({ json: trading }),
  );
  const main = page.getByRole('main');
  const selectDay = async (day, accept = true) => {
    await main.getByRole('button', { name: /choose date/i }).click();
    await page.clock.runFor(300);
    await page
      .getByRole('gridcell', { name: String(day), exact: true })
      .click();
    await page
      .getByRole('button', { name: accept ? 'OK' : 'Cancel', exact: true })
      .click();
    await page.clock.runFor(300);
  };
  try {
    await page.clock.install({ time: new Date(at) });
    await page.goto(`${url}/dashboard`);
    await main
      .locator('[data-testid="dashboard-day-net"]')
      .getByText('THB 12.25', { exact: true })
      .waitFor();
    await main
      .getByText('reviewer synthetic research summary.', { exact: true })
      .waitFor();
    assert.equal(
      await main.getByText(/99,999/).count(),
      0,
      'Paper is excluded from real P/L.',
    );
    assert(
      (
        await main
          .getByRole('link', { name: 'Read reports', exact: true })
          .getAttribute('href')
      ).includes(`view=team-chat#run-${run.id}`),
    );
    await selectDay(19, false);
    assert.equal(
      await main.locator('[data-date]').getAttribute('data-date'),
      date,
      'Mobile Cancel keeps the selected day.',
    );
    assert(
      !dates.includes('2026-10-19'),
      'Cancelled day does not trigger a query.',
    );
    await selectDay(19);
    await main.locator('[data-date="2026-10-19"]').waitFor();
    await main
      .getByText('No confirmed engine result is available for this day.', {
        exact: true,
      })
      .waitFor();
    assert(
      dates.includes('2026-10-19'),
      'Date-only history query uses selected Bangkok day.',
    );
    assert(
      (
        await main.locator('[data-testid="total-assets"]').textContent()
      ).includes('12,500.00'),
      'Historical selection retains current wallet scope.',
    );
    await selectDay(20);
    await main
      .getByTestId('dashboard-day-net')
      .getByText('THB 12.25', { exact: true })
      .waitFor();
    if (process.env.CAPTURE_DASHBOARD !== '0') {
      await fs.mkdir('.impeccable/review', { recursive: true });
      for (const [name, width, height] of [
        ['desktop', 1440, 1000],
        ['tablet', 768, 1024],
        ['mobile', 390, 844],
      ]) {
        await page.setViewportSize({ width, height });
        assert(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
          ),
        );
        assert(
          await main
            .locator('[data-testid]')
            .evaluateAll((values) =>
              values.every(
                (value) => value.scrollWidth <= value.clientWidth + 1,
              ),
            ),
          `${name}: metric amounts stay within their regions.`,
        );
        await page.screenshot({
          path: `.impeccable/review/dashboard-${name}.png`,
          fullPage: true,
          animations: 'disabled',
        });
      }
    }
    portfolio = {
      ...portfolio,
      summary: { totalAssets: null, availableThb: null, holdingCount: null },
      updatedAt: null,
      priceUpdatedAt: null,
    };
    trading = {
      ...trading,
      fault: 'Synthetic engine fault',
      live: {
        ...book,
        orders: [
          {
            id: 'fixture-order',
            symbol: 'BTC',
            side: 'buy',
            amount: '100',
            limitPrice: '10',
            status: 'unknown',
            createdAt: at,
            brokerOrderId: null,
            reason: 'Synthetic uncertainty',
            remaining: '100',
          },
        ],
      },
    };
    await page.setViewportSize({ width: 1440, height: 1000 });
    await main
      .getByRole('button', { name: 'Refresh summary', exact: true })
      .click();
    await main.getByText('Trading needs attention', { exact: true }).waitFor();
    await main
      .getByText('1 live order outcome(s) need reconciliation', { exact: true })
      .waitFor();
    assert.deepEqual(writes, []);
    assert.deepEqual(errors, []);
    console.log(
      'Dashboard UI: real/paper scope, unknown history, current wallet, Bangkok calendar, report link, actual faults, unknown orders, responsive and GET-only passed.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
