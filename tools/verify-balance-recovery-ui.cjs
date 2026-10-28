const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');
const ts = require('typescript');
const syncFs = require('node:fs');
require.extensions['.ts'] = (module, filename) =>
  module._compile(
    ts.transpileModule(syncFs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
        esModuleInterop: true,
      },
    }).outputText,
    filename,
  );
const {
  tradingOverviewSchema,
} = require('../apps/web/features/trading/contracts/TradingOverview.ts');
const {
  portfolioSchema,
} = require('../apps/web/features/crypto/contracts/PortfolioResponse.ts');
const {
  historyResponseSchema,
} = require('../apps/web/features/crypto/contracts/HistoryResponse.ts');

const base = process.env.TEST_URL ?? 'http://127.0.0.1:3000';
assert.match(base, /^http:\/\/127\.0\.0\.1:(3000|3108|3112)$/);
const output = process.env.RECONCILIATION_CAPTURE_DIR;
assert(output, 'RECONCILIATION_CAPTURE_DIR is required.');

const account = 'a'.repeat(64);
const rulesHash = 'b'.repeat(64);
const stoppedMandate = {
  id: 'fixture-mandate',
  accountKey: account,
  rulesHash,
  createdAt: '2026-10-20T00:00:00Z',
  expiresAt: '2026-10-30T00:00:00Z',
  revokedAt: '2026-10-20T00:00:00Z',
};
const rules = {
  version: 'adaptive-intraday-v2',
  symbols: [],
  allocatedThb: 5000,
  perOrderThb: 1000,
  maxPositions: 3,
  dailyLossThb: 500,
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
  adaptive: {
    universeVersion: 'bitkub-thb-spot-v1',
    includeFutureDeposits: true,
    excludedSymbols: [],
    dailyLossFraction: 0.1,
    dailyProfitTargetThb: 1000,
    risk: {
      profile: 'aggressive',
      maximumExposureFraction: 0.5,
      maximumConcentrationFraction: 0.25,
      riskToStopFraction: 0.1,
      atrMultiplier: 2,
      maximumSpreadFraction: 0.005,
      estimatedFeeRate: 0.0025,
      maximumDataAgeSeconds: 90,
      maximumSignalAgeSeconds: 300,
    },
  },
};

function scenario() {
  const data = structuredClone(fixture.data);
  const overview = data['/api/trading'];
  Object.assign(overview, {
    accountKey: account,
    mode: 'stopped',
    rules,
    rulesHash,
    mandate: stoppedMandate,
    emergencyStopped: true,
    liveActivationAvailable: true,
    live: {
      ...overview.live,
      cashThb: '40',
      orders: [],
      positions: [
        {
          symbol: 'WLD',
          quantity: '2.50001902',
          costThb: '100',
          openedAt: '2026-10-20T03:00:00Z',
          peakBid: '40',
        },
      ],
    },
  });
  data['/api/crypto/history'] = {
    trades: [
      {
        transactionId: 'sell-wld-1',
        symbol: 'WLD',
        side: 'sell',
        quantity: 2.5,
        timestamp: '2026-10-21T03:00:00Z',
        price: 40,
        fee: 0,
        feeCredit: 0,
      },
    ],
    coverage: 'Fixture history',
  };
  data['/api/crypto/portfolio'] = {
    ...data['/api/crypto/portfolio'],
    updatedAt: new Date().toISOString(),
    positions: [
      {
        symbol: 'WLD',
        name: 'Worldcoin',
        status: 'Closed',
        quantity: 0,
        averageCost: null,
        currentPrice: 40,
        value: 0,
        unrealizedPnl: null,
        realizedPnl: null,
        costStatus: 'Unknown',
      },
    ],
    summary: { totalAssets: 0, availableThb: 5000, holdingCount: 0 },
    performance: {
      ...data['/api/crypto/portfolio'].performance,
      unreconciledSymbols: ['WLD'],
      notice: 'Cost and profit are unverified.',
    },
  };
  data['/api/team'].accountKey = account;
  tradingOverviewSchema.parse(overview);
  portfolioSchema.parse(data['/api/crypto/portfolio']);
  historyResponseSchema.parse(data['/api/crypto/history']);
  return data;
}

async function verify() {
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  try {
    for (const [device, width, height, startMode] of [
      ['desktop', 1440, 1000, 'stopped'],
      ['tablet', 834, 1100, 'stopped'],
      ['mobile', 390, 844, 'stopped'],
      ['small-phone', 320, 740, 'stopped'],
      ['manual-desktop', 1440, 1000, 'manualReview'],
      ['manual-tablet', 834, 1100, 'manualReview'],
      ['manual-mobile', 390, 844, 'manualReview'],
      ['manual-small-phone', 320, 740, 'manualReview'],
    ]) {
      const context = await browser.newContext({
        viewport: { width, height },
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
      });
      const page = await context.newPage();
      page.setDefaultTimeout(12000);
      const corruptedProof = startMode === 'stopped';
      if (corruptedProof) {
        await page.addInitScript((key) => {
          sessionStorage.setItem(
            `quantara-reconciliation-pending:${key}`,
            '{corrupted proof',
          );
        }, account);
      }
      const data = scenario();
      data['/api/trading'].mode = startMode;
      data['/api/trading'].emergencyStopped = startMode === 'stopped';
      const stopPosts = startMode === 'manualReview' ? 1 : 0;
      const posts = [];
      const errors = [];
      const lostActivationResponse = device === 'desktop';
      let selectedActivationExpiry = null;
      let releaseManualSale = null;
      let manualSaleResponse = null;
      page.on('pageerror', (error) => errors.push(error.message));
      await context.routeWebSocket('**/*', (socket) => {
        errors.push('Unexpected WebSocket blocked');
        socket.close();
      });
      await context.route('**/*', async (route) => {
        const request = route.request();
        const url = new URL(request.url());
        if (url.origin !== base) {
          errors.push('External request blocked');
          return route.abort();
        }
        if (!url.pathname.startsWith('/api/')) {
          assert.equal(
            request.method(),
            'GET',
            'Static requests must be read-only.',
          );
          return process.env.STATIC_BUILD === '1'
            ? staticBuildAsset(route, url)
            : route.continue();
        }
        const method = request.method();
        if (method !== 'GET')
          posts.push({
            method,
            path: url.pathname,
            body: request.postDataJSON(),
          });
        if (method !== 'GET' && method !== 'POST') {
          return route.fulfill({
            status: 404,
            json: { error: 'Unexpected method' },
          });
        }
        if (
          method === 'POST' &&
          url.pathname.endsWith('/manual-sale/preview')
        ) {
          assert.deepEqual(request.postDataJSON(), {
            symbol: 'WLD',
            executionId: 'sell-wld-1',
          });
          return route.fulfill({
            status: 200,
            json: {
              symbol: 'WLD',
              allocatedSoldQuantity: '2.5',
              unallocatedHistoricalQuantity: '0.00001902',
              allocatedCostThb: null,
              realizedNetProfitThb: null,
              brokerBalance: { symbol: 'WLD', quantity: '0', available: '0' },
              cashSettlementPending: true,
              brokerObservedAt: new Date().toISOString(),
              expiresAt: new Date(Date.now() + 300000).toISOString(),
              proof: 'sale-proof',
              acknowledgement: 'Confirm sold WLD',
              applied: false,
            },
          });
        }
        if (
          method === 'POST' &&
          url.pathname.endsWith('/cash-baseline/preview')
        ) {
          return route.fulfill({
            status: 200,
            json: {
              accountKey: account,
              previousCashThb: '40',
              cashThb: '5000',
              availableThb: '5000',
              currentDayProfitUnknown: false,
              brokerObservedAt: new Date().toISOString(),
              expiresAt: new Date(Date.now() + 300000).toISOString(),
              proof: 'cash-proof',
              acknowledgement: 'Confirm current cash',
              applied: false,
            },
          });
        }
        if (
          method === 'POST' &&
          (url.pathname.endsWith('/manual-sale/apply') ||
            url.pathname.endsWith('/cash-baseline/apply'))
        ) {
          const body = request.postDataJSON();
          assert.equal(
            body.proof,
            url.pathname.includes('cash-baseline')
              ? 'cash-proof'
              : 'sale-proof',
          );
          assert.equal(
            body.acknowledgement,
            url.pathname.includes('cash-baseline')
              ? 'Confirm current cash'
              : 'Confirm sold WLD',
          );
          if (url.pathname.includes('cash-baseline')) {
            data['/api/trading'].live.cashThb = '5000';
            data['/api/trading'].live.positions = [];
            data['/api/trading'].autonomy = null;
          } else {
            data['/api/trading'].live.positions = [];
            if (manualSaleResponse === null) {
              manualSaleResponse = new Promise((resolve) => {
                releaseManualSale = resolve;
              });
              await manualSaleResponse;
            }
          }
          return route.fulfill({
            status: 200,
            json: {
              ...(url.pathname.includes('cash-baseline')
                ? {
                    accountKey: account,
                    previousCashThb: '40',
                    cashThb: '5000',
                    availableThb: '5000',
                    currentDayProfitUnknown: false,
                  }
                : {
                    symbol: 'WLD',
                    allocatedSoldQuantity: '2.5',
                    unallocatedHistoricalQuantity: '0.00001902',
                    allocatedCostThb: null,
                    realizedNetProfitThb: null,
                    brokerBalance: {
                      symbol: 'WLD',
                      quantity: '0',
                      available: '0',
                    },
                    cashSettlementPending: true,
                  }),
              brokerObservedAt: new Date().toISOString(),
              expiresAt: new Date(Date.now() + 300000).toISOString(),
              proof: url.pathname.includes('cash-baseline')
                ? 'cash-proof'
                : 'sale-proof',
              acknowledgement: 'Confirm',
              applied: true,
            },
          });
        }
        if (url.pathname === '/api/trading/disable' && method === 'POST') {
          assert.deepEqual(route.request().postDataJSON(), {});
          data['/api/trading'].mode = 'stopped';
          data['/api/trading'].emergencyStopped = false;
          return route.fulfill({ json: data['/api/trading'] });
        }
        if (url.pathname === '/api/trading/activate' && method === 'POST') {
          const body = request.postDataJSON();
          assert(
            selectedActivationExpiry,
            'The owner must select an expiry first.',
          );
          assert.deepEqual(body, {
            accountKey: account,
            rulesHash,
            expiresAt: selectedActivationExpiry,
            acknowledgement: 'enable-auto-trading',
          });
          const expiry = Date.parse(body.expiresAt);
          assert(expiry > Date.now() && expiry <= Date.now() + 30 * 86400000);
          assert.equal(data['/api/trading'].mode, 'stopped');
          assert.equal(data['/api/trading'].emergencyStopped, false);
          assert.deepEqual(data['/api/trading'].live.positions, []);
          assert.equal(data['/api/trading'].live.cashThb, '5000');
          Object.assign(data['/api/trading'], {
            mode: 'autoLive',
            heartbeatAt: new Date().toISOString(),
            fault: null,
            mandate: {
              id: 'synthetic-enabled-mandate',
              accountKey: account,
              rulesHash,
              createdAt: new Date().toISOString(),
              expiresAt: body.expiresAt,
              revokedAt: null,
            },
          });
          tradingOverviewSchema.parse(data['/api/trading']);
          // Only the synthetic response is lost; no backend or broker is called.
          return lostActivationResponse
            ? route.abort('connectionreset')
            : route.fulfill({ json: data['/api/trading'] });
        }
        if (method !== 'GET') {
          errors.push(`Unexpected synthetic write: ${method} ${url.pathname}`);
          return route.fulfill({
            status: 404,
            json: { error: 'Unexpected API request' },
          });
        }
        const value = data[url.pathname];
        if (!value) errors.push(`Unmocked synthetic read: ${url.pathname}`);
        return route.fulfill({
          status: value ? 200 : 404,
          json: value ?? { error: 'Unmocked fixture API' },
        });
      });
      await page.goto(`${base}/crypto`);
      const reviewBalances = page.getByRole('button', {
        name: 'Review balances',
        exact: true,
      });
      await reviewBalances.waitFor();
      const collapse = page.getByRole('button', {
        name: 'Collapse team chat',
        exact: true,
      });
      const expand = page.getByRole('button', {
        name: 'Expand team chat',
        exact: true,
      });
      await collapse.or(expand).waitFor();
      if (await collapse.isVisible()) await collapse.click();
      await expand.waitFor();
      await reviewBalances.click();
      const dialog = page.getByRole('dialog', { name: 'Balance review' });
      if (corruptedProof) {
        await dialog
          .getByText('Saved confirmation could not be read.', { exact: false })
          .waitFor();
        assert.equal(
          await dialog.getByText('Bitkub account', { exact: false }).count(),
          1,
        );
        assert.equal(
          posts.length,
          0,
          'Malformed saved confirmation must not trigger a write.',
        );
        assert.equal(
          await dialog.getByLabel('Strategy lot').count(),
          0,
          'Recovery presents its available action without an unusable form.',
        );
        await dialog.screenshot({
          path: path.join(output, `${device}-malformed-confirmation.png`),
        });
        await dialog
          .getByRole('button', {
            name: 'Discard saved confirmation',
            exact: true,
          })
          .click();
        await dialog
          .getByText('Saved confirmation discarded locally', { exact: false })
          .waitFor();
        assert.equal(
          posts.length,
          0,
          'Discarding malformed confirmation must not call the server.',
        );
        await dialog
          .getByRole('button', { name: 'Refresh status', exact: true })
          .click();
      }
      if (startMode === 'manualReview') {
        const pause = dialog.getByRole('button', {
          name: 'Pause trading to review',
          exact: true,
        });
        await pause.waitFor();
        assert.equal(posts.length, 0, 'Opening review never stops trading.');
        assert.equal(await dialog.getByLabel('Strategy lot').count(), 0);
        await dialog.screenshot({
          path: path.join(output, `${device}-pause-review.png`),
        });
        await pause.click();
      }
      const confirmSale = dialog.getByRole('button', {
        name: 'Confirm sold holdings',
        exact: true,
      });
      await confirmSale.waitFor();
      assert.equal(await confirmSale.isEnabled(), false);
      assert.equal(
        posts.length,
        1 + stopPosts,
        'Preview must never confirm automatically.',
      );
      await dialog.getByText('0.00001902 WLD', { exact: true }).waitFor();
      await dialog.screenshot({
        path: path.join(output, `${device}-sold-review.png`),
      });
      await dialog.getByRole('checkbox').check();
      await dialog
        .getByRole('button', {
          name: 'Confirm sold holdings',
          exact: true,
        })
        .click();
      await dialog
        .getByText('Waiting for the server to verify this review.', {
          exact: true,
        })
        .waitFor();
      assert.equal(
        await dialog
          .getByText('The confirmation may already be saved', { exact: false })
          .count(),
        0,
        'The first confirmation shows pending progress, not an uncertain-result warning.',
      );
      assert.equal(
        await dialog.getByRole('checkbox').isDisabled(),
        true,
        'Acknowledgement cannot change while recording is in progress.',
      );
      assert.equal(
        await dialog
          .getByRole('button', { name: 'Please wait…', exact: true })
          .isDisabled(),
        true,
        'The confirm action is disabled while recording is in progress.',
      );
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
        'Pending confirmation has no horizontal overflow.',
      );
      await dialog.screenshot({
        path: path.join(output, `${device}-pending-confirmation.png`),
      });
      assert.equal(typeof releaseManualSale, 'function');
      releaseManualSale();
      await dialog
        .getByRole('button', { name: 'Confirm cash balance', exact: true })
        .waitFor();
      assert.equal(
        await dialog
          .getByRole('button', {
            name: 'Confirm cash balance',
            exact: true,
          })
          .isEnabled(),
        false,
      );
      assert.equal(
        posts.length,
        3 + stopPosts,
        'Cash review also needs its own explicit confirmation.',
      );
      await dialog.screenshot({
        path: path.join(output, `${device}-cash-review.png`),
      });
      await dialog.getByRole('checkbox').check();
      await dialog
        .getByRole('button', {
          name: 'Confirm cash balance',
          exact: true,
        })
        .click();
      await dialog.waitFor({ state: 'hidden' });
      const reviewEnable = page.getByRole('button', {
        name: 'Review & enable',
        exact: true,
      });
      assert.equal(
        await reviewEnable.isEnabled(),
        startMode === 'manualReview',
      );
      assert.equal(
        data['/api/trading'].emergencyStopped,
        startMode === 'stopped',
      );
      assert.equal(
        posts.length,
        4 + stopPosts,
        'Accounting review never changes authority.',
      );
      if (startMode === 'stopped') {
        await page
          .getByRole('button', { name: 'Clear emergency stop', exact: true })
          .click();
      }
      await page.getByText('Auto trading off', { exact: true }).waitFor();
      assert.equal(await reviewEnable.isEnabled(), true);
      assert.equal(data['/api/trading'].mode, 'stopped');
      await reviewEnable.click();
      assert.equal(
        await page.getByRole('dialog', { name: 'Enable auto trading' }).count(),
        1,
      );
      await page
        .getByRole('dialog', { name: 'Enable auto trading' })
        .screenshot({ path: path.join(output, `${device}-mandate.png`) });
      if (device === 'desktop') {
        const mandate = page.getByRole('dialog', {
          name: 'Enable auto trading',
        });
        await mandate
          .getByRole('button', { name: '1 day', exact: true })
          .click();
        const enable = mandate.getByRole('button', {
          name: 'Enable Auto Trading',
          exact: true,
        });
        assert.equal(await enable.isEnabled(), true);
        data['/api/trading'].live.positions =
          scenario()['/api/trading'].live.positions;
        await page.waitForResponse(
          (response) => new URL(response.url()).pathname === '/api/trading',
          { timeout: 20000 },
        );
        await mandate
          .getByText('WLD holdings differ', { exact: false })
          .waitFor();
        assert.equal(
          await enable.isDisabled(),
          true,
          'A new balance discrepancy explains and disables activation.',
        );
        assert.equal(
          posts.length,
          5,
          'Polling the review never activates trading.',
        );
        await mandate.screenshot({
          path: path.join(output, 'desktop-mandate-balance-blocker.png'),
        });
        data['/api/trading'].live.positions = [];
        await page.waitForResponse(
          (response) => new URL(response.url()).pathname === '/api/trading',
          { timeout: 20000 },
        );
        await mandate
          .getByText('WLD holdings differ', { exact: false })
          .waitFor({ state: 'hidden' });
        assert.equal(
          await enable.isEnabled(),
          true,
          'A verified matching balance clears the blocker.',
        );
      }
      await page.getByRole('button', { name: 'Cancel', exact: true }).click();
      assert.equal(posts.length, 5);
      assert.equal(data['/api/trading'].emergencyStopped, false);
      assert.equal(data['/api/trading'].mode, 'stopped');
      assert.deepEqual(data['/api/trading'].mandate, stoppedMandate);
      assert.equal(
        await page
          .getByText('Historical cost and profit remain unverified: WLD.', {
            exact: true,
          })
          .count(),
        1,
      );
      assert.deepEqual(errors, []);
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth > innerWidth,
        ),
        false,
      );
      await page.screenshot({
        path: path.join(output, `${device}-balance-review.png`),
        fullPage: true,
      });
      // Complete the connected owner journey after proving Cancel changes no authority.
      await reviewEnable.click();
      const mandate = page.getByRole('dialog', {
        name: 'Enable auto trading',
      });
      const enable = mandate.getByRole('button', {
        name: 'Enable Auto Trading',
        exact: true,
      });
      await enable.waitFor();
      assert.equal(
        await enable.isDisabled(),
        true,
        'A new review needs its own expiry.',
      );
      await mandate.getByRole('button', { name: '1 day', exact: true }).click();
      selectedActivationExpiry = await mandate
        .getByLabel('Mandate expiry (your local time)')
        .evaluate((input) => new Date(input.value).toISOString());
      assert.equal(await enable.isEnabled(), true);
      assert.equal(
        posts.length,
        5,
        'Selecting expiry must not enable trading.',
      );
      await enable.click();
      await page.getByText('Auto trading on', { exact: true }).waitFor();
      await mandate.waitFor({ state: 'hidden' });
      assert.equal(
        posts.length,
        6,
        'Only the final explicit click enables the synthetic account.',
      );
      assert.equal(
        posts.filter((post) => post.path === '/api/trading/activate').length,
        1,
        'A lost response must not cause an automatic activation retry.',
      );
      if (lostActivationResponse) {
        await page.getByText('Check latest status', { exact: true }).waitFor();
      }
      await page.screenshot({
        path: path.join(output, `${device}-enabled.png`),
        fullPage: true,
      });
      await page.reload();
      await page.getByText('Auto trading on', { exact: true }).waitFor();
      assert.equal(
        posts.length,
        6,
        'Reload reads the retained state without another command.',
      );
      assert.equal(data['/api/trading'].mandate.revokedAt, null);
      assert.deepEqual(errors, []);
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth > innerWidth,
        ),
        false,
        'The enabled view fits the viewport.',
      );
      results.push({
        device,
        startMode,
        posts: posts.map((post) => post.path),
        noOverflow: true,
        syntheticEnableConfirmed: true,
        activationResponse: lostActivationResponse
          ? 'lost-then-read'
          : 'received',
        activationRequests: 1,
        reloadWithoutWrite: true,
      });
      await context.close();
    }
    await fs.writeFile(
      path.join(output, 'results.json'),
      JSON.stringify({ results }, null, 2),
    );
    console.log(JSON.stringify({ results }));
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
