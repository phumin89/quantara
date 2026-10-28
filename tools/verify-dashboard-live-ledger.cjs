const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

const testUrl = process.env.TEST_URL || 'http://127.0.0.1:3000';
assert.match(testUrl, /^http:\/\/127\.0\.0\.1:(3000|3107|3108)$/);
const captureDir =
  process.env.DASHBOARD_CAPTURE_DIR ||
  'work/dashboard-capital-review-20261025/browser';

function trading(mode, currentNet) {
  const base = structuredClone(fixture.data['/api/trading']);
  base.mode = mode;
  base.heartbeatAt = fixture.at;
  base.live = { ...base.live, equityThb: '777' };
  base.reports = [
    {
      ...base.reports[0],
      date: fixture.date,
      mode: 'autoLive',
      revision: 1,
      endingEquityThb: '700',
      realizedNetThb: '5',
    },
    {
      ...base.reports[0],
      date: '2026-10-19',
      mode: 'autoLive',
      revision: 1,
      endingEquityThb: '690',
      realizedNetThb: '4',
    },
  ];
  base.autonomy = {
    observedAt: fixture.at,
    universeAt: fixture.at,
    totalMarkets: 1,
    eligibleMarkets: 1,
    checkedMarkets: 1,
    activity: 'Fixture monitoring',
    nextCheckAt: fixture.at,
    capital: {
      observedAt: fixture.at,
      availableThb: '777',
      equityThb: '777',
      exposureThb: '0',
      reservedThb: '0',
      blocker: null,
    },
    readiness: null,
    funnel: {
      date: fixture.date,
      coverage: 'complete',
      signals: 1,
      submittedOrders: 1,
      filledOrders: 1,
      closedTrades: 1,
      netThb: currentNet,
      blockedReasons: [],
    },
    candidates: [],
    evaluation: null,
    opportunityReview: {
      protocol: 'observed-book-v1',
      sampleLimit: 8,
      assumptions: 'Fixture only.',
      days: [],
    },
    dailyProfitTargetThb: '100',
    realizedNetTodayThb: currentNet,
  };
  return base;
}

async function verify() {
  await fs.mkdir(captureDir, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  try {
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 834, 1112],
      ['phone', 390, 844],
    ]) {
      for (const scenario of [
        ...['stopped', 'manualReview', 'autoLive', 'paper'].map((mode) => ({
          mode,
          unknown: false,
        })),
        { mode: 'stopped', unknown: true },
      ]) {
        const { mode, unknown } = scenario;
        const context = await browser.newContext({
          viewport: { width, height },
          serviceWorkers: 'block',
          reducedMotion: 'reduce',
        });
        const page = await context.newPage();
        page.setDefaultTimeout(10000);
        const writes = [],
          escaped = [],
          errors = [],
          unmocked = [];
        const currentNet = unknown ? null : mode === 'paper' ? '99999' : '42';
        const current = trading(mode, currentNet);
        await context.route('**/*', async (route) => {
          const request = route.request();
          const url = new URL(request.url());
          if (url.origin !== testUrl) {
            escaped.push(url.origin);
            return route.abort();
          }
          if (!url.pathname.startsWith('/api/')) return route.continue();
          if (request.method() !== 'GET') {
            writes.push(url.pathname);
            return route.fulfill({
              status: 405,
              json: { error: 'Fixture writes forbidden' },
            });
          }
          const body =
            url.pathname === '/api/trading'
              ? current
              : fixture.data[url.pathname];
          if (!body) unmocked.push(url.pathname);
          return route.fulfill({
            status: body ? 200 : 404,
            json: body || { error: 'Unmocked fixture route' },
          });
        });
        page.on('pageerror', (error) => errors.push(error.message));
        await page.clock.install({ time: new Date(fixture.at) });
        await page.goto(`${testUrl}/dashboard`);
        const main = page.getByRole('main');
        await main
          .getByRole('heading', { name: 'Live realized net P/L', exact: true })
          .waitFor();
        const equitySection = main.locator('section').filter({
          has: page.getByRole('heading', {
            name: 'Live engine equity',
            exact: true,
          }),
        });
        const netSection = main.locator('section').filter({
          has: page.getByRole('heading', {
            name: 'Live realized net P/L',
            exact: true,
          }),
        });
        for (const section of [equitySection, netSection]) {
          const summary = section.getByText('Chart data', { exact: true });
          await summary.focus();
          await summary.press('Enter');
        }
        const equityRow = equitySection
          .getByRole('row')
          .filter({ hasText: fixture.date });
        const netRow = netSection
          .getByRole('row')
          .filter({ hasText: fixture.date });
        await equityRow.waitFor();
        await netRow.waitFor();
        const equityText = (
          await equityRow.getByRole('cell').nth(1).textContent()
        ).replace(/\u00a0/g, ' ');
        const rowText = (
          await netRow.getByRole('cell').nth(1).textContent()
        ).replace(/\u00a0/g, ' ');
        const expectedNet = unknown
          ? 'Unavailable'
          : mode === 'paper'
            ? 'THB 5.00'
            : 'THB 42.00';
        assert.equal(rowText, expectedNet);
        assert.equal(
          equityText,
          mode === 'paper' ? 'THB 700.00' : 'THB 777.00',
        );
        const equityKpi = main.getByTestId('dashboard-equity');
        const netKpi = main.getByTestId('dashboard-day-net');
        const equityKpiText = await equityKpi.textContent();
        const netKpiText = await netKpi.textContent();
        assert.equal(equityKpiText.replace(/\u00a0/g, ' '), 'THB 15,000.00');
        assert.equal(netKpiText.replace(/\u00a0/g, ' '), expectedNet);
        assert.equal(writes.length, 0);
        assert.deepEqual(escaped, []);
        assert.deepEqual(errors, []);
        assert.deepEqual(unmocked, []);
        assert.equal(
          await page.evaluate(
            () =>
              document.documentElement.scrollWidth <=
              document.documentElement.clientWidth + 1,
          ),
          true,
        );
        if (mode === 'stopped' && !unknown) {
          const collapseChat = page.getByRole('button', {
            name: 'Collapse team chat',
            exact: true,
          });
          if (await collapseChat.isVisible()) {
            await collapseChat.click();
            await page
              .getByRole('button', { name: 'Expand team chat' })
              .waitFor();
          }
          await page.evaluate(() => scrollTo(0, 0));
          await page.screenshot({
            path: `${captureDir}/dashboard-live-ledger-${name}.png`,
            fullPage: true,
          });
        }
        results.push({ name, mode, unknown, row: rowText, equity: equityText });
        await context.close();
      }
    }
    const summary = { results, writes: 0, externalOrigins: 0 };
    await fs.writeFile(
      `${captureDir}/dashboard-live-ledger-results.json`,
      JSON.stringify(summary, null, 2),
    );
    console.log(JSON.stringify(summary, null, 2));
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
