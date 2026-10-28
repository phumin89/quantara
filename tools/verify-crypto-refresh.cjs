const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function verify() {
  const url = process.env.TEST_URL;
  assert(
    url === 'http://127.0.0.1:3108',
    'Use only the isolated Crypto fixture server on port 3108.',
  );
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
  });
  page.setDefaultTimeout(10000);
  const writes = [],
    errors = [];
  let reads = 0;
  let account = 'a';
  let failure = false;
  let hold = false;
  let heldObserved = null;
  const held = [];
  let wallet = structuredClone(fixture.data['/api/crypto/portfolio']);
  const portfolio = () => structuredClone(wallet);
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== 'GET') {
      writes.push(path);
      await route.fulfill({
        status: 405,
        json: { error: 'Writes are forbidden in this fixture.' },
      });
      return;
    }
    if (path === '/api/crypto/portfolio') {
      reads++;
      if (hold) {
        held.push({ route, body: portfolio() });
        heldObserved?.();
        return;
      }
      await route.fulfill({
        status: failure ? 503 : 200,
        json: failure
          ? { error: 'Synthetic portfolio unavailable' }
          : portfolio(),
      });
      return;
    }
    const body = structuredClone(fixture.data[path]);
    if (!body) {
      await route.fulfill({
        status: 404,
        json: { error: 'Unmocked fixture API' },
      });
      return;
    }
    if (path === '/api/team') {
      body.latestRun.accountKey = `synthetic-crypto-${account}`;
    } else if (path === '/api/team/brief') {
      body.date = new URL(request.url()).searchParams.get('date');
      body.runs.forEach((run) => {
        run.accountKey = `synthetic-crypto-${account}`;
      });
    } else if (path === '/api/trading') {
      body.mode = 'manualReview';
    } else if (path === '/api/crypto/history') {
      body.trades = [
        {
          transactionId: `synthetic-${account}`,
          symbol: new URL(request.url()).searchParams.get('coin'),
          side: 'buy',
          quantity: 1,
          price: account === 'a' ? 111 : 222,
          fee: 0,
          feeCredit: 0,
          timestamp: fixture.at,
        },
      ];
    }
    await route.fulfill({ json: body });
  });
  page.on('pageerror', (error) => errors.push(error.message));
  const main = page.getByRole('main');
  const refresh = async () => {
    const response = page.waitForResponse(
      (item) => new URL(item.url()).pathname === '/api/crypto/portfolio',
    );
    await main.getByRole('button', { name: 'Refresh', exact: true }).click();
    await page.clock.runFor(1000);
    await response;
    await main.getByRole('button', { name: 'Refresh', exact: true }).waitFor();
  };
  try {
    await page.clock.install({ time: new Date(fixture.at) });
    await page.goto(`${url}/crypto`);
    await main.getByTestId('total-assets').waitFor();
    const collapseChat = page.getByRole('button', {
      name: 'Collapse team chat',
      exact: true,
    });
    if (await collapseChat.isVisible()) {
      await collapseChat.click();
      await page.clock.runFor(500);
      await page.getByRole('button', { name: 'Expand team chat' }).waitFor();
    }
    const before = reads;
    await refresh();
    await page.waitForFunction(
      () => !document.querySelector('main [aria-busy="true"]'),
    );
    assert.deepEqual(
      writes,
      [],
      'Refresh must use GET only; no sync or trading commands.',
    );
    assert(reads > before, 'Refresh reloads the existing portfolio query.');
    await main.getByRole('button', { name: 'Disable', exact: true }).waitFor();
    await main
      .getByRole('button', { name: 'Emergency stop', exact: true })
      .waitFor();
    assert.equal(
      await main.getByRole('columnheader').count(),
      5,
      'The default holdings table exposes only core fields.',
    );
    assert.equal(
      await main
        .getByRole('button', { name: 'Holding', exact: true })
        .getAttribute('aria-pressed'),
      'true',
    );
    await main
      .getByRole('button', { name: 'Snapshot status: Current', exact: true })
      .waitFor();
    await page.clock.runFor(125000);
    await main
      .getByRole('button', { name: 'Snapshot status: Stale', exact: true })
      .waitFor();
    await refresh();
    await main
      .getByRole('button', { name: 'Snapshot status: Stale', exact: true })
      .waitFor();
    assert.equal(
      wallet.updatedAt,
      fixture.at,
      'A fetch clock never becomes a source wallet timestamp.',
    );
    failure = true;
    await refresh();
    await main.getByText(/Synthetic portfolio unavailable/).waitFor();
    assert.match(
      await main.getByTestId('total-assets').textContent(),
      /12,500\.00/,
    );
    failure = false;
    const now = await page.evaluate(() => new Date().toISOString());
    wallet.updatedAt = now;
    wallet.priceUpdatedAt = now;
    await refresh();
    await main
      .getByRole('button', { name: 'Snapshot status: Current', exact: true })
      .waitFor();
    const fresh = portfolio();
    wallet.updatedAt = fixture.at;
    wallet.summary.totalAssets = 999;
    await refresh();
    await main.getByText(/An older snapshot was received/).waitFor();
    assert.match(
      await main.getByTestId('total-assets').textContent(),
      /12,500\.00/,
      'Out-of-order source data cannot replace newer balances.',
    );
    wallet = structuredClone(fresh);
    await refresh();
    wallet.updatedAt = new Date(Date.parse(now) + 300000).toISOString();
    await refresh();
    await main.getByText(/snapshot timestamp is in the future/).waitFor();
    wallet = structuredClone(fresh);
    await refresh();
    await main
      .getByRole('button', { name: 'View BTC history', exact: true })
      .click();
    await main
      .getByRole('region', { name: 'BTC trade history', exact: true })
      .getByText(/111\.00/)
      .waitFor();
    hold = true;
    const heldReady = new Promise((resolve) => {
      heldObserved = resolve;
    });
    await main.getByRole('button', { name: 'Refresh', exact: true }).click();
    await heldReady;
    account = 'b';
    hold = false;
    wallet.summary.totalAssets = 42000;
    wallet.summary.availableThb = 1000;
    wallet.positions = wallet.positions.map((position) => ({
      ...position,
      name: 'Synthetic B ' + position.symbol,
    }));
    await page.clock.runFor(10100);
    await main
      .getByTestId('total-assets')
      .filter({ hasText: /42,000\.00/ })
      .waitFor();
    for (const pending of held.splice(0))
      await pending.route.fulfill({ json: pending.body }).catch(() => {});
    assert.match(
      await main.getByTestId('total-assets').textContent(),
      /42,000\.00/,
      'Late prior-account requests cannot restore prior balances.',
    );
    assert.equal(
      await main
        .getByRole('button', { name: 'View BTC history', exact: true })
        .getAttribute('aria-expanded'),
      'false',
      'An account context change clears cached/expanded history.',
    );
    await main
      .getByRole('button', { name: 'View BTC history', exact: true })
      .click();
    await main
      .getByRole('region', { name: 'BTC trade history', exact: true })
      .getByText(/222\.00/)
      .waitFor();
    const sold = portfolio();
    const soldAt = await page.evaluate(() => new Date().toISOString());
    sold.updatedAt = soldAt;
    sold.priceUpdatedAt = soldAt;
    sold.historyUpdatedAt = soldAt;
    sold.summary = { totalAssets: 0, availableThb: 15000, holdingCount: 0 };
    sold.positions = sold.positions.map((position) => ({
      ...position,
      quantity: 0,
      value: 0,
      status: 'Closed',
      unrealizedPnl: null,
      realizedPnl: null,
    }));
    wallet = sold;
    await refresh();
    await main
      .getByText('No assets currently held in this snapshot.', { exact: true })
      .waitFor();
    assert.match(
      await main.getByTestId('available-thb').textContent(),
      /15,000\.00/,
    );
    await main.getByRole('button', { name: 'Closed', exact: true }).click();
    assert.equal(
      await main.getByRole('button', { name: /^View .* history$/ }).count(),
      2,
      'A changed balance snapshot retains closed assets without inventing trades or P/L.',
    );
    wallet = structuredClone(fresh);
    wallet.updatedAt = await page.evaluate(() => new Date().toISOString());
    wallet.priceUpdatedAt = wallet.updatedAt;
    await refresh();
    await main.getByRole('button', { name: 'Holding', exact: true }).click();
    if (process.env.CAPTURE === '1') {
      await fs.mkdir('.impeccable/review', { recursive: true });
      for (const [name, width, height] of [
        ['desktop', 1440, 1000],
        ['tablet', 768, 1024],
        ['mobile', 390, 844],
      ]) {
        await page.setViewportSize({ width, height });
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.clock.runFor(300);
        assert(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= window.innerWidth,
          ),
          `${name} must not overflow the page.`,
        );
        await page.screenshot({
          path: `.impeccable/review/crypto-refresh-${name}.png`,
          fullPage: true,
        });
      }
    }
    assert.deepEqual(
      writes,
      [],
      'Polling, refresh, history and account changes send no commands.',
    );
    assert.deepEqual(errors, [], 'No unhandled browser errors.');
    console.log(
      'Crypto: GET-only refresh, source age/failure/future/out-of-order guards, account-switch late-read/history isolation, changed balance snapshot, visible trading controls and responsive fixtures passed.',
    );
  } finally {
    for (const pending of held) await pending.route.abort().catch(() => {});
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
