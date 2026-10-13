const assert = require('node:assert/strict');
const { chromium } = require('playwright');

async function verifyContracts() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  try {
    const page = await browser.newPage();
    const invalidSymbol = await page.request.get(
      `${url}/api/crypto/history?coin=bad%2Fsymbol`,
    );
    assert.equal(invalidSymbol.status(), 400);
    assert.deepEqual(await invalidSymbol.json(), {
      error: 'Invalid coin symbol.',
    });
    const crossOrigin = await page.request.post(`${url}/api/crypto/sync`, {
      headers: { origin: 'http://untrusted.invalid' },
    });
    assert.equal(crossOrigin.status(), 403);
    assert.deepEqual(await crossOrigin.json(), {
      error: 'Cross-origin sync is not allowed.',
    });
    const errors = [];
    page.on('pageerror', (error) => errors.push(error.message));
    let payload = { positions: 'invalid API data' };
    await page.route('**/api/crypto/portfolio', (route) =>
      route.fulfill({ json: payload }),
    );
    await page.goto(`${url}/crypto`);
    await page.getByRole('button', { name: 'Retry', exact: true }).waitFor();
    await page
      .getByRole('button', { name: 'Bitkub status: Error', exact: true })
      .waitFor();
    assert.equal(await page.getByTestId('total-assets').count(), 0);
    // Fixtures exist only in this isolated test browser.
    payload = {
      positions: [],
      summary: { totalAssets: null, availableThb: null, holdingCount: null },
      updatedAt: null,
      priceUpdatedAt: null,
      historyUpdatedAt: null,
      historyCoverage: 'Test fixture',
      syncing: false,
      completedSymbols: 0,
      totalSymbols: 0,
      error: null,
    };
    await page.getByRole('button', { name: 'Retry', exact: true }).click();
    await page
      .getByRole('button', { name: 'Bitkub status: Checking', exact: true })
      .waitFor();
    for (const id of ['total-assets', 'available-thb', 'holding-count'])
      assert.equal(await page.getByTestId(id).textContent(), '—');
    const fixturePosition = {
      symbol: 'TEST',
      name: 'Test coin',
      status: 'Holding',
      quantity: 1,
      averageCost: null,
      currentPrice: 100,
      value: 100,
      unrealizedPnl: null,
      realizedPnl: null,
      costStatus: 'Unknown',
    };
    const fixturePortfolio = payload;
    for (const status of ['Unsupported', 0]) {
      payload = {
        ...fixturePortfolio,
        positions: [{ ...fixturePosition, status }],
      };
      await page.reload();
      await page.getByRole('button', { name: 'Retry', exact: true }).waitFor();
      assert.equal(await page.getByTestId('total-assets').count(), 0);
    }
    payload = { ...fixturePortfolio, positions: [fixturePosition] };
    let side = 'hold';
    await page.route('**/api/crypto/history?*', (route) =>
      route.fulfill({
        json: {
          trades: [
            {
              transactionId: 'test-1',
              symbol: 'TEST',
              side,
              quantity: 1,
              price: 100,
              fee: 0,
              feeCredit: 0,
              timestamp: '2026-01-01T00:00:00Z',
            },
          ],
          coverage: 'Test fixture',
        },
      }),
    );
    for (const invalidSide of ['hold', 0]) {
      side = invalidSide;
      await page.reload();
      await page.getByRole('button', { name: 'View TEST history' }).click();
      await page
        .getByText('History response is invalid.', { exact: true })
        .waitFor();
    }
    side = 'buy';
    await page.getByRole('button', { name: 'Retry', exact: true }).click();
    await page.getByText('buy', { exact: true }).waitFor();
    console.log(
      'Invalid status/side enums rejected; valid lowercase trade side recovers.',
    );
    assert.deepEqual(errors, []);
    console.log(
      'Malformed API payload rejected; valid unknown data recovers without invented values: passed',
    );
  } finally {
    await browser.close();
  }
}
verifyContracts().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
