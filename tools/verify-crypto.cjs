const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verifyCrypto() {
  const url = process.env.TEST_URL;
  assert.equal(
    url,
    'http://127.0.0.1:3108',
    'Only the isolated frontend fixture server is allowed.',
  );
  assert(
    process.argv.includes('--fixtures'),
    'All API requests must use fixtures.',
  );
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
  const portfolioRetry = page
    .locator('main button:not(#trading button)')
    .filter({ hasText: /^Retry$/ });
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await fs.mkdir('.impeccable/review', { recursive: true });
  const fixture = process.argv.includes('--fixtures')
    ? {
        positions: [
          {
            symbol: 'TEST',
            name: 'Fixture holding',
            status: 'Holding',
            quantity: 2,
            averageCost: 90.25,
            currentPrice: 100,
            value: 200,
            unrealizedPnl: 19.5,
            realizedPnl: 8.25,
            costStatus: 'Verified',
            lastBuyPrice: 90,
            lastBoughtAt: '2026-10-19T11:20:00+07:00',
          },
          {
            symbol: 'OLD',
            name: 'Fixture closed',
            status: 'Closed',
            quantity: 0,
            averageCost: null,
            currentPrice: 50,
            value: 0,
            unrealizedPnl: null,
            realizedPnl: null,
            costStatus: 'Unknown',
          },
          {
            symbol: 'LOSS',
            name: 'Fixture loss',
            status: 'Closed',
            quantity: 0,
            averageCost: null,
            currentPrice: 50,
            value: 0,
            unrealizedPnl: null,
            realizedPnl: -12.34,
            costStatus: 'Verified',
          },
          {
            symbol: 'LEGACY',
            name: 'Fixture imported holding',
            status: 'Holding',
            quantity: 1,
            averageCost: null,
            currentPrice: 50,
            value: 50,
            unrealizedPnl: null,
            realizedPnl: null,
            costStatus: 'Unknown',
          },
        ],
        summary: { totalAssets: 250, availableThb: 1500, holdingCount: 2 },
        performance: {
          scope: 'confirmedEngineFills',
          realizedPnl: -4.09,
          unrealizedPnl: null,
          totalPnl: null,
          fees: 0.75,
          asOf: '2026-10-19T11:20:00+07:00',
          unreconciledSymbols: ['LEGACY'],
          notice:
            'Engine fills only. Imported holdings have unknown cost. Unrealized P/L excludes future exit fees.',
        },
        updatedAt: '2026-01-01T00:00:00Z',
        priceUpdatedAt: '2026-01-01T00:00:00Z',
        historyUpdatedAt: '2026-01-01T00:00:00Z',
        historyCoverage: 'Isolated test fixture',
        syncing: false,
        completedSymbols: 0,
        totalSymbols: 0,
        error: null,
      }
    : null;
  const fulfillFixture = (route) => route.fulfill({ json: fixture });
  const readSnapshot = async () =>
    fixture ?? (await page.request.get(`${url}/api/crypto/portfolio`)).json();
  if (fixture) {
    await page.route('**/api/**', (route) => {
      assert.equal(
        route.request().method(),
        'GET',
        'The browser fixture must never send a command.',
      );
      return route.fulfill({
        status: 404,
        json: { error: 'Unmocked fixture API' },
      });
    });
    await page.route('**/api/crypto/portfolio', fulfillFixture);
    await page.route('**/api/crypto/risk', (route) =>
      route.fulfill({
        json: {
          accountKey: 'fixture-account',
          policyHash: 'a'.repeat(64),
          policy: {
            profile: 'disabled',
            maximumExposureFraction: 1,
            maximumConcentrationFraction: 1,
            riskToStopFraction: 0.05,
            atrMultiplier: 1,
            maximumSpreadFraction: 0.005,
            estimatedFeeRate: 0.0025,
            maximumDataAgeSeconds: 90,
            maximumSignalAgeSeconds: 600,
          },
          capital: null,
          performance: null,
          refreshError: null,
          manualOnly: true,
        },
      }),
    );
    await page.route('**/api/crypto/history?*', (route) =>
      route.fulfill({
        json: {
          trades: ['buy', 'sell'].map((side, index) => ({
            transactionId: `fixture-${index}`,
            symbol: 'TEST',
            side,
            quantity: 1,
            price: 100,
            fee: 0.1,
            feeCredit: 0,
            timestamp: '2026-01-01T00:00:00Z',
          })),
          coverage: 'Isolated test fixture',
        },
      }),
    );
  }
  try {
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.goto(`${url}/crypto`);
      await page
        .getByRole('columnheader', { name: 'Coin', exact: true })
        .waitFor();
      if (width < 600) {
        const region = page.getByRole('region', {
          name: 'Crypto holdings and trading history',
        });
        await region.focus();
        await page.keyboard.press('ArrowRight');
        await page.waitForFunction(
          () =>
            document.querySelector(
              '[aria-label="Crypto holdings and trading history"]',
            ).scrollLeft > 0,
        );
        await region.evaluate((element) => {
          element.scrollLeft = 0;
        });
      }
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
      );
      const snapshot = await readSnapshot();
      if (fixture) {
        assert.equal(await page.getByRole('columnheader').count(), 5);
        const verifiedRow = page.getByRole('row').filter({
          has: page.getByRole('button', {
            name: 'View TEST history',
            exact: true,
          }),
        });
        assert.match(
          await verifiedRow.getByRole('cell').nth(4).textContent(),
          /200\.00/,
        );
        assert.equal(
          await verifiedRow.getByText('Unknown cost', { exact: true }).count(),
          0,
        );
        await page
          .getByRole('button', { name: 'View TEST history', exact: true })
          .click();
        const verified = page.getByRole('region', {
          name: 'TEST engine cost and performance',
          exact: true,
        });
        await verified.getByText('19 Oct, 11:20', { exact: true }).waitFor();
        await verified.getByText(/90\.25/).waitFor();
        assert.equal(
          await verified
            .getByText(/19\.50/)
            .evaluate((element) => getComputedStyle(element).color),
          'rgb(166, 209, 188)',
          'Verified gains use the Dusk success color.',
        );
        await page
          .getByRole('button', { name: 'View LEGACY history', exact: true })
          .click();
        const legacy = page.getByRole('region', {
          name: 'LEGACY engine cost and performance',
          exact: true,
        });
        await legacy.getByText('Unknown cost', { exact: true }).waitFor();
        assert.doesNotMatch(
          await legacy.innerText(),
          /0\.00/,
          'Unknown amounts must not be rendered as zero.',
        );
        await page.getByRole('button', { name: 'Closed', exact: true }).click();
        await page
          .getByRole('button', { name: 'View LOSS history', exact: true })
          .click();
        const loss = page.getByRole('region', {
          name: 'LOSS engine cost and performance',
          exact: true,
        });
        assert.equal(
          await loss
            .getByText(/12\.34/)
            .evaluate((element) => getComputedStyle(element).color),
          'rgb(242, 166, 169)',
          'Verified losses use the Dusk error color.',
        );
      }
      const formattedMoney = (value) =>
        value == null
          ? '—'
          : `฿${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
      await page
        .getByRole('region', { name: 'Portfolio summary', exact: true })
        .waitFor();
      const expectedSummary = [
        ['total-assets', formattedMoney(snapshot.summary.totalAssets)],
        ['available-thb', formattedMoney(snapshot.summary.availableThb)],
        [
          'holding-count',
          snapshot.summary.holdingCount == null
            ? '—'
            : String(snapshot.summary.holdingCount),
        ],
      ];
      for (const filter of ['Holding', 'Closed', 'All']) {
        await page.getByRole('button', { name: filter, exact: true }).click();
        const expected = snapshot.positions.filter(
          (position) => filter === 'All' || position.status === filter,
        ).length;
        assert.equal(
          await page.getByRole('button', { name: /^View .* history$/ }).count(),
          expected,
        );
        for (const [id, value] of expectedSummary)
          assert.equal(
            await page.getByTestId(id).textContent(),
            value,
            `${id} stays independent of the table filter`,
          );
      }
      if (snapshot.positions.length) {
        const symbol = snapshot.positions[0].symbol;
        await page
          .getByRole('button', { name: `View ${symbol} history`, exact: true })
          .click();
        await page
          .getByRole('region', { name: `${symbol} trade history`, exact: true })
          .getByRole('columnheader', { name: 'Date', exact: true })
          .waitFor();
      }
      await page.mouse.move(0, 0);
      await page.evaluate(() => window.scrollTo(0, 0));
      if (process.env.CAPTURE === '1') {
        await page.screenshot({
          path: `.impeccable/review/crypto-${name}.png`,
          fullPage: true,
        });
      }
      console.log(
        `${name}: ${fixture ? 'isolated fixtures' : 'real data'}, filters, history and overflow passed`,
      );
    }
    if (fixture) {
      const legacyFixture = structuredClone(fixture);
      delete legacyFixture.performance;
      await page.route('**/api/crypto/portfolio', (route) =>
        route.fulfill({ json: legacyFixture }),
      );
      await page.reload();
      await page
        .getByRole('columnheader', { name: 'Coin', exact: true })
        .waitFor();
      await page
        .getByRole('button', { name: 'View LEGACY history', exact: true })
        .click();
      await page
        .getByRole('region', {
          name: 'LEGACY engine cost and performance',
          exact: true,
        })
        .getByText('Unknown cost', { exact: true })
        .waitFor();
      assert.equal(await page.getByTestId('engine-total-pnl').count(), 0);
      await page.unroute('**/api/crypto/portfolio');
      await page.route('**/api/crypto/portfolio', fulfillFixture);
      console.log(
        'Verified engine values, losses, unknown imported cost and legacy performance response: passed',
      );
    }
    // Failure simulation is confined to this test browser, never the product.
    await page.route('**/api/crypto/portfolio', (route) =>
      route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Test: backend unavailable' }),
      }),
    );
    await page.reload();
    await portfolioRetry.waitFor();
    assert.equal(
      await page.getByText('Test: backend unavailable').isVisible(),
      true,
    );
    await page.unroute('**/api/crypto/portfolio');
    if (fixture) {
      await page.route('**/api/crypto/portfolio', fulfillFixture);
    }
    await portfolioRetry.click();
    await page
      .getByRole('columnheader', { name: 'Coin', exact: true })
      .waitFor();
    const saved = await readSnapshot();
    let badgeSnapshot = { ...saved, syncing: false, error: null };
    await page.route('**/api/crypto/portfolio', (route) =>
      route.fulfill({ json: badgeSnapshot }),
    );
    await page.reload();
    const connected = page.getByRole('button', {
      name: 'Snapshot status: Stale',
      exact: true,
    });
    await connected.click();
    await page.getByRole('tooltip').waitFor();
    assert.match(await page.getByRole('tooltip').textContent(), /Snapshot/);
    assert.match(await page.getByRole('tooltip').textContent(), /Prices/);
    assert.match(await page.getByRole('tooltip').textContent(), /History/);
    await page.keyboard.press('Escape');
    await page.getByRole('tooltip').waitFor({ state: 'hidden' });
    badgeSnapshot = { ...saved, syncing: true, error: null };
    await page.reload();
    await page
      .getByRole('button', { name: 'Snapshot status: Syncing', exact: true })
      .waitFor();
    badgeSnapshot = {
      ...saved,
      syncing: false,
      error: 'Test: exchange sync failed',
    };
    await page.reload();
    await page
      .getByRole('button', { name: 'Snapshot status: Error', exact: true })
      .waitFor();
    await portfolioRetry.waitFor();
    assert.equal(
      await page
        .getByRole('region', { name: 'Portfolio summary', exact: true })
        .isVisible(),
      true,
    );
    badgeSnapshot = { ...saved, syncing: false, error: null };
    await portfolioRetry.click();
    await page
      .getByRole('button', { name: 'Snapshot status: Stale', exact: true })
      .waitFor();
    assert.deepEqual(errors, []);
    if (fixture) {
      const pollingPage = await browser.newPage({
        viewport: { width: 1440, height: 900 },
      });
      const pollingSnapshot = structuredClone(fixture);
      const writes = [];
      let portfolioReads = 0;
      let holdPoll = false;
      let pendingPoll;
      let observedHeldPoll;
      const waitForHeldPoll = () =>
        new Promise((resolve, reject) => {
          const deadline = setTimeout(
            () => reject(new Error('Portfolio GET poll was not observed')),
            5000,
          );
          observedHeldPoll = () => {
            clearTimeout(deadline);
            resolve();
          };
        });
      pollingPage.on('request', (request) => {
        if (request.url().includes('/api/') && request.method() !== 'GET') {
          writes.push(request.method());
        }
      });
      await pollingPage.route('**/api/**', (route) =>
        route.fulfill({ status: 404, json: { error: 'Isolated fixture API' } }),
      );
      await pollingPage.route('**/api/crypto/portfolio', (route) => {
        portfolioReads += 1;
        if (holdPoll) {
          pendingPoll = route;
          observedHeldPoll();
          return;
        }
        return route.fulfill({ json: pollingSnapshot });
      });
      await pollingPage.clock.install();
      await pollingPage.goto(`${url}/crypto`);
      await pollingPage
        .getByRole('button', { name: 'Snapshot status: Stale', exact: true })
        .waitFor();
      assert.equal(portfolioReads, 1);
      holdPoll = true;
      const heldPollReady = waitForHeldPoll();
      await pollingPage.clock.fastForward(15_000);
      await heldPollReady;
      assert.equal(
        portfolioReads,
        2,
        'Settled portfolios keep refreshing through GET',
      );
      await pollingPage.clock.fastForward(6000);
      assert.equal(
        portfolioReads,
        2,
        'A delayed portfolio GET never overlaps the next poll',
      );
      pollingSnapshot.summary.totalAssets = 300;
      holdPoll = false;
      await pendingPoll.fulfill({ json: pollingSnapshot });
      await pollingPage
        .getByTestId('total-assets')
        .filter({ hasText: '฿300.00' })
        .waitFor();
      holdPoll = true;
      const unmountPollReady = waitForHeldPoll();
      await pollingPage.clock.fastForward(15_000);
      await unmountPollReady;
      const abortedPoll = pollingPage.waitForEvent('requestfailed', {
        predicate: (request) => request.url().endsWith('/api/crypto/portfolio'),
      });
      await pollingPage
        .getByRole('link', { name: 'Home', exact: true })
        .click();
      await pollingPage
        .getByRole('heading', { name: 'Home', exact: true })
        .waitFor();
      await abortedPoll;
      await pendingPoll.fulfill({ json: pollingSnapshot }).catch(() => {});
      const readsAtUnmount = portfolioReads;
      await pollingPage.clock.fastForward(45_000);
      assert.equal(
        portfolioReads,
        readsAtUnmount,
        'Unmount stops the portfolio read timer',
      );
      assert.deepEqual(
        writes,
        [],
        'Automatic portfolio refresh never posts a sync or financial action',
      );
      await pollingPage.close();
      console.log(
        'Settled portfolio GET polling, delayed-read serialization and unmount abort/stop: passed',
      );
    }
    console.log(
      'Connection badge, timestamps, Escape and saved-snapshot retry: passed',
    );
    console.log('Backend failure/retry and browser errors: passed');
  } finally {
    await browser.close();
  }
}
verifyCrypto().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
