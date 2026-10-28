const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');

const base = process.env.TEST_URL;
assert(base, 'TEST_URL is required.');
assert.match(base, /^http:\/\/127\.0\.0\.1:(3000|3108|3112)$/);
const output = process.env.RECONCILIATION_CAPTURE_DIR;
assert(output, 'RECONCILIATION_CAPTURE_DIR is required.');
const account = 'a'.repeat(64);
const observedAt = new Date(Date.now() - 3600000).toISOString();
const expiresAt = new Date(Date.parse(observedAt) + 300000).toISOString();

function saved(kind) {
  const binding = {
    accountKey: account,
    rulesHash: 'b'.repeat(64),
    material: 'saved-proof-material',
    previousCashThb: '1000',
    riskSummary: 'Saved confirmation retained for recovery.',
    originalLot: null,
  };
  const result =
    kind === 'manual-sale'
      ? {
          symbol: 'BTC',
          allocatedSoldQuantity: '1',
          unallocatedHistoricalQuantity: '0',
          allocatedCostThb: null,
          realizedNetProfitThb: null,
          brokerBalance: { symbol: 'BTC', quantity: '0', available: '0' },
          brokerObservedAt: observedAt,
          expiresAt,
          proof: 'manual-proof',
          acknowledgement: 'Confirm sold BTC',
          applied: false,
          cashSettlementPending: false,
        }
      : {
          accountKey: account,
          cashThb: '1000',
          availableThb: '1000',
          brokerObservedAt: observedAt,
          expiresAt,
          proof: 'cash-proof',
          acknowledgement: 'Confirm current cash',
          applied: false,
          currentDayProfitUnknown: false,
        };
  if (kind === 'manual-sale')
    binding.originalLot = {
      symbol: 'BTC',
      quantity: '1',
      costThb: '1000',
      openedAt: new Date(Date.parse(observedAt) - 86400000).toISOString(),
    };
  return { kind, result, binding };
}

async function verify() {
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  const errors = [],
    escapes = [],
    unmocked = [];
  try {
    for (const [device, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 834, 1180],
      ['phone', 390, 844],
      ['smallphone', 320, 800],
    ]) {
      for (const kind of ['manual-sale', 'cash-baseline']) {
        const context = await browser.newContext({
          viewport: { width, height },
          reducedMotion: 'reduce',
          serviceWorkers: 'block',
        });
        const page = await context.newPage();
        page.setDefaultTimeout(12000);
        const posts = [];
        page.on('pageerror', (error) => errors.push(error.message));
        await page.addInitScript(
          (value) =>
            sessionStorage.setItem(
              `quantara-reconciliation-pending:${value.account}`,
              JSON.stringify(value.saved),
            ),
          { account, saved: saved(kind) },
        );
        const data = structuredClone(fixture.data);
        Object.assign(data['/api/trading'], {
          accountKey: account,
          mode: 'paper',
          rulesHash: 'c'.repeat(64),
          mandate: null,
          emergencyStopped: false,
          live: { ...data['/api/trading'].live, positions: [], orders: [] },
          paper: { ...data['/api/trading'].paper, positions: [], orders: [] },
        });
        data['/api/team'].accountKey = account;
        await context.routeWebSocket('**/*', (socket) => {
          errors.push('Unexpected WebSocket blocked');
          socket.close();
        });
        await context.route('**/*', async (route) => {
          const request = route.request();
          const url = new URL(request.url());
          if (url.origin !== base) {
            escapes.push(url.origin);
            return route.abort();
          }
          if (!url.pathname.startsWith('/api/')) {
            if (request.method() === 'GET') {
              return process.env.STATIC_BUILD === '1'
                ? staticBuildAsset(route, url)
                : route.continue();
            }
            unmocked.push(`${request.method()} ${url.pathname}`);
            return route.abort();
          }
          const expectedPath = `/api/trading/reconciliation/manual-sale/${kind === 'cash-baseline' ? 'cash-baseline/' : ''}apply`;
          if (request.method() === 'POST' && url.pathname === expectedPath) {
            const body = request.postDataJSON();
            assert.deepEqual(body, {
              proof: kind === 'manual-sale' ? 'manual-proof' : 'cash-proof',
              acknowledgement:
                kind === 'manual-sale'
                  ? 'Confirm sold BTC'
                  : 'Confirm current cash',
            });
            posts.push(url.pathname);
            return route.fulfill({
              status: 200,
              json: { ...saved(kind).result, applied: true },
            });
          }
          if (request.method() !== 'GET') {
            unmocked.push(`${request.method()} ${url.pathname}`);
            return route.abort();
          }
          const value = data[url.pathname];
          if (!value) {
            unmocked.push(url.pathname);
            return route.abort();
          }
          return route.fulfill({ status: 200, json: value });
        });
        const initialStatus = page.waitForResponse(
          (response) => new URL(response.url()).pathname === '/api/trading',
        );
        await page.goto(`${base}/crypto`);
        await initialStatus;
        const collapse = page.getByRole('button', {
          name: 'Collapse team chat',
          exact: true,
        });
        if ((await collapse.count()) && (await collapse.isVisible()))
          await collapse.click();
        assert.deepEqual(posts, []);
        await page.screenshot({
          path: path.join(output, `${device}-${kind}-before-review.png`),
          fullPage: true,
        });
        await page
          .getByRole('button', { name: 'Review balances', exact: true })
          .click();
        const dialog = page.getByRole('dialog', { name: 'Balance review' });
        await dialog.waitFor();
        assert.equal(
          await dialog
            .getByRole('button', {
              name: 'Retry same confirmation',
              exact: true,
            })
            .isDisabled(),
          true,
        );
        assert.equal(
          await page
            .getByRole('button', {
              name: 'Pause trading to review',
              exact: true,
            })
            .count(),
          0,
        );
        const refreshedStatus = page.waitForResponse(
          (response) => new URL(response.url()).pathname === '/api/trading',
        );
        await page
          .getByRole('button', { name: 'Refresh status', exact: true })
          .click();
        await refreshedStatus;
        await dialog
          .getByRole('button', { name: 'Retry same confirmation', exact: true })
          .waitFor();
        assert.deepEqual(posts, []);
        await dialog.screenshot({
          path: path.join(output, `${device}-${kind}-saved-recovery.png`),
        });
        const bounds = await dialog.boundingBox();
        assert(
          bounds &&
            bounds.x >= 0 &&
            bounds.y >= 0 &&
            bounds.x + bounds.width <= width &&
            bounds.y + bounds.height <= height,
          'Dialog exceeds viewport bounds.',
        );
        await dialog.getByRole('checkbox').check();
        await dialog
          .getByRole('button', { name: 'Retry same confirmation', exact: true })
          .click();
        await page
          .getByTestId('reconciliation-preview')
          .waitFor({ state: 'hidden' });
        if (kind === 'cash-baseline') {
          await dialog.waitFor({ state: 'hidden' });
        } else {
          await dialog
            .getByText('The server confirmed this accounting change.', {
              exact: false,
            })
            .waitFor();
        }
        assert.equal(
          await page.evaluate(
            (key) => sessionStorage.getItem(key),
            `quantara-reconciliation-pending:${account}`,
          ),
          null,
          'Successful recovery must clear the saved proof.',
        );
        assert.deepEqual(posts, [
          kind === 'manual-sale'
            ? '/api/trading/reconciliation/manual-sale/apply'
            : '/api/trading/reconciliation/manual-sale/cash-baseline/apply',
        ]);
        assert.equal(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
          ),
          true,
        );
        results.push({ device, kind, posts, noOverflow: true });
        await context.close();
      }
    }
    const result = { results, errors, escapes, unmocked };
    assert.deepEqual(errors, []);
    assert.deepEqual(escapes, []);
    assert.deepEqual(unmocked, []);
    await fs.writeFile(
      path.join(output, 'results.json'),
      JSON.stringify(result, null, 2),
    );
    console.log(JSON.stringify(result));
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
