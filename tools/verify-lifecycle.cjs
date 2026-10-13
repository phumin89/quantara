const assert = require('node:assert/strict');
const { chromium } = require('playwright');

async function verify() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  try {
    const page = await browser.newPage({
      viewport: { width: 1440, height: 900 },
    });
    const errors = [];
    page.on('pageerror', (error) => errors.push(error.message));
    const snapshot = {
      positions: [
        {
          symbol: 'TEST',
          name: 'Fixture',
          status: 'Holding',
          quantity: 1,
          averageCost: null,
          currentPrice: 1,
          value: 1,
          unrealizedPnl: null,
          realizedPnl: null,
          costStatus: 'Unknown',
        },
      ],
      summary: { totalAssets: 1, availableThb: 0, holdingCount: 1 },
      updatedAt: '2026-01-01T00:00:00Z',
      priceUpdatedAt: '2026-01-01T00:00:00Z',
      historyUpdatedAt: '2026-01-01T00:00:00Z',
      historyCoverage: 'Fixture',
      syncing: true,
      completedSymbols: 0,
      totalSymbols: 1,
      error: null,
    };
    let count = 0;
    let pendingPoll;
    await page.route('**/api/crypto/portfolio', async (route) => {
      count++;
      if (count === 2) {
        pendingPoll = route;
        return;
      }
      await route.fulfill({ json: snapshot });
    });
    await page.goto(`${url}/crypto`);
    await page
      .getByRole('button', { name: 'Bitkub status: Syncing' })
      .waitFor();
    await page.waitForRequest('**/api/crypto/portfolio');
    // Hold one poll beyond the next interval; no second request may start.
    await page.waitForTimeout(2200);
    assert.equal(count, 2);
    assert.ok(pendingPoll);
    snapshot.syncing = false;
    await pendingPoll.fulfill({ json: snapshot });
    await page
      .getByRole('button', { name: 'Bitkub status: Connected' })
      .waitFor();
    await page.waitForTimeout(2200);
    assert.equal(count, 2, 'Polling stops when syncing finishes');

    let pendingHistory;
    let historyCount = 0;
    await page.route('**/api/crypto/history?*', async (route) => {
      historyCount++;
      if (historyCount === 1) {
        pendingHistory = route;
        return;
      }
      await route.fulfill({ json: { trades: [], coverage: 'New fixture' } });
    });
    const historyButton = page.getByRole('button', {
      name: 'View TEST history',
    });
    const requestedHistory = page.waitForRequest('**/api/crypto/history?*');
    await historyButton.click();
    await requestedHistory;
    await page.getByText('Loading history…', { exact: true }).waitFor();
    assert.ok(pendingHistory);
    const abortedHistory = page.waitForEvent('requestfailed', {
      predicate: (request) => request.url().includes('/api/crypto/history?'),
    });
    await page.route('**/api/crypto/sync', (route) => {
      snapshot.historyUpdatedAt = '2026-01-02T00:00:00Z';
      return route.fulfill({ json: {} });
    });
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await abortedHistory;
    await page.getByRole('button', { name: 'Refresh', exact: true }).waitFor();
    assert.equal(await historyButton.getAttribute('aria-expanded'), 'false');
    await pendingHistory
      .fulfill({ json: { trades: [], coverage: 'Stale fixture' } })
      .catch(() => {});
    await historyButton.click();
    await page
      .getByText('No verified trades found.', { exact: true })
      .waitFor();
    assert.equal(
      historyCount,
      2,
      'New history must load after snapshot changes',
    );

    let pendingSync;
    await page.route('**/api/crypto/sync', (route) => {
      pendingSync = route;
    });
    const syncRequest = page.waitForRequest('**/api/crypto/sync');
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await syncRequest;
    const beforeLeaving = count;
    const abortedSync = page.waitForEvent('requestfailed', {
      predicate: (request) => request.url().endsWith('/api/crypto/sync'),
    });
    await page.getByRole('link', { name: 'Home', exact: true }).click();
    await page.getByRole('heading', { name: 'Home', exact: true }).waitFor();
    await abortedSync;
    await pendingSync.fulfill({ json: {} }).catch(() => {});
    // A completed/aborted sync must not start another portfolio read after unmount.
    await page.waitForTimeout(100);
    assert.equal(count, beforeLeaving);
    assert.deepEqual(errors, []);
    console.log(
      'Class lifecycle: polling serialization/stop, history invalidation and unmount cancellation passed.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
