const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function verify() {
  const base = process.env.TEST_URL;
  assert.match(base ?? '', /^http:\/\/127\.0\.0\.1:(3108|3112)$/);
  const output = process.env.RECONCILIATION_CAPTURE_DIR;
  assert(output, 'An explicit private capture directory is required.');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  const errors = [];
  const escaped = [];
  try {
    for (const [device, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 820, 1180],
      ['mobile', 390, 844],
    ]) {
      const context = await browser.newContext({
        viewport: { width, height },
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
      });
      const page = await context.newPage();
      page.setDefaultTimeout(12000);
      page.on('pageerror', (error) => errors.push(error.message));
      const data = structuredClone(fixture.data);
      const account = 'a'.repeat(64);
      Object.assign(data['/api/trading'], {
        accountKey: account,
        mode: 'stopped',
        rulesHash: 'b'.repeat(64),
        mandate: {
          id: 'fixture-mandate',
          accountKey: account,
          rulesHash: 'b'.repeat(64),
          createdAt: new Date().toISOString(),
          expiresAt: new Date(Date.now() + 3600000).toISOString(),
          revokedAt: new Date().toISOString(),
        },
      });
      if (data['/api/team']) data['/api/team'].accountKey = account;
      const reconciliationCalls = [];
      await context.route('**/*', async (route) => {
        const request = route.request();
        const url = new URL(request.url());
        if (url.origin !== base) {
          escaped.push(url.origin);
          await route.abort();
          return;
        }
        if (url.pathname.startsWith('/api/')) {
          assert.equal(
            request.method(),
            'GET',
            'Crypto removal regression must be read-only.',
          );
          if (url.pathname.startsWith('/api/trading/reconciliation/'))
            reconciliationCalls.push(url.pathname);
          const value = data[url.pathname];
          await route.fulfill({
            status: value ? 200 : 404,
            json: value ?? { error: 'Unmocked fixture API' },
          });
          return;
        }
        await route.continue();
      });
      await page.goto(base + '/crypto');
      const chatControl = page.getByRole('button', {
        name: 'Expand team chat',
        exact: true,
      });
      await chatControl.waitFor();
      assert.equal(await chatControl.getAttribute('aria-expanded'), 'false');
      await page
        .getByRole('heading', { name: 'Crypto', exact: true })
        .waitFor();
      await page
        .getByRole('button', { name: 'Refresh', exact: true })
        .waitFor();
      assert.equal(
        await page
          .getByRole('region', { name: 'Reconcile strategy accounting' })
          .count(),
        0,
      );
      assert.equal(
        await page
          .getByText('Reconcile strategy accounting', { exact: true })
          .count(),
        0,
      );
      for (const name of [
        'Reload reconciliation status',
        'Preview inventory archival',
        'Preview current cash',
        'Load imported sells',
      ]) {
        assert.equal(
          await page.getByRole('button', { name, exact: true }).count(),
          0,
        );
      }
      await page
        .getByRole('heading', { name: 'Trading', exact: true })
        .waitFor();
      await page.locator('#data-scope').waitFor();
      assert.deepEqual(reconciliationCalls, []);
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth > innerWidth,
        ),
        false,
      );
      await page.screenshot({
        path: path.join(output, device + '-removed.png'),
        fullPage: true,
      });
      results.push({
        device,
        reconciliationPanelAbsent: true,
        tradingVisible: true,
        walletVisible: true,
        reconciliationCalls: 0,
      });
      await context.close();
    }
    assert.deepEqual(errors, []);
    assert.deepEqual(escaped, []);
    const result = { results, escaped, errors, syntheticOnly: true };
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
