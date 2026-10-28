const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function verify() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const output = '.impeccable/review';
  await fs.mkdir(output, { recursive: true });
  try {
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 820, 1180],
      ['phone', 390, 844],
    ]) {
      const page = await browser.newPage({
        viewport: { width, height },
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
      });
      page.setDefaultTimeout(10000);
      const writes = [];
      const errors = [];
      let phase = 'error';
      let pending;
      let reads = 0;
      page.on('pageerror', (error) => errors.push(error.message));
      await page.route('**/api/**', async (route) => {
        const request = route.request();
        const target = new URL(request.url());
        if (request.method() !== 'GET') {
          writes.push(target.pathname);
          await route.fulfill({
            status: 405,
            json: { error: 'Fixture is read-only.' },
          });
          return;
        }
        if (target.pathname === '/api/crypto/portfolio') {
          reads++;
          if (phase === 'pending') {
            pending = route;
            return;
          }
          if (phase === 'error') {
            await route.fulfill({
              status: 503,
              json: { error: 'Unable to load portfolio.' },
            });
            return;
          }
          if (phase === 'html') {
            await route.fulfill({
              status: 502,
              contentType: 'text/html',
              body: '<html>internal-fixture-marker</html>',
            });
            return;
          }
        }
        const body = structuredClone(fixture.data[target.pathname]);
        if (target.pathname === '/api/team/brief' && body) {
          body.date = target.searchParams.get('date');
        }
        await route.fulfill({
          status: body ? 200 : 404,
          json: body ?? { error: 'Fixture route unavailable.' },
        });
      });
      await page.clock.install({ time: new Date(fixture.at) });
      await page.goto(`${url}/crypto`);
      const collapse = page.getByRole('button', {
        name: 'Collapse team chat',
        exact: true,
      });
      const main = page.getByRole('main');
      const retry = main.getByRole('button', { name: 'Retry', exact: true });
      await retry.waitFor();
      if (await collapse.isVisible()) {
        await collapse.click();
        await page
          .getByRole('button', { name: 'Expand team chat', exact: true })
          .waitFor();
      }
      assert.equal(
        await main.getByTestId('total-assets').count(),
        0,
        'Initial failure never invents a wallet balance.',
      );
      phase = 'pending';
      const beforeRetry = reads;
      const requested = page.waitForRequest('**/api/crypto/portfolio');
      await retry.click();
      await requested;
      const busy = main.getByRole('button', { name: /^Retry/ });
      assert.equal(
        await busy.isEnabled(),
        false,
        'Retry must stay disabled while its portfolio read is pending.',
      );
      assert.match(await busy.textContent(), /Retrying/);
      assert.equal(
        await main
          .getByRole('button', { name: 'Refreshing', exact: true })
          .isEnabled(),
        false,
      );
      assert.equal(reads, beforeRetry + 1, 'Retry starts one read.');
      await page.screenshot({
        path: `${output}/portfolio-retrying-${name}.png`,
      });
      phase = 'success';
      await pending.fulfill({ json: fixture.data['/api/crypto/portfolio'] });
      await main.getByTestId('total-assets').waitFor();
      assert.equal(
        await main
          .getByText('Unable to load portfolio.', { exact: true })
          .count(),
        0,
      );
      phase = 'html';
      await main.getByRole('button', { name: 'Refresh', exact: true }).click();
      await main.getByText(/Showing the last saved snapshot/).waitFor();
      const message = await main.innerText();
      assert.match(message, /Unable to load portfolio/);
      assert.doesNotMatch(
        message,
        /internal-fixture-marker|SyntaxError|Unexpected token/,
      );
      assert.match(
        await main.getByTestId('total-assets').innerText(),
        /12,500\.00/,
      );
      assert.equal(await retry.isEnabled(), true);
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth + 1,
        ),
        'No page overflow.',
      );
      await page.screenshot({
        path: `${output}/portfolio-recovery-${name}.png`,
      });
      assert.deepEqual(
        writes,
        [],
        'Every API is intercepted; recovery is GET-only.',
      );
      assert.deepEqual(errors, []);
      await page.close();
    }
    console.log(
      'Portfolio recovery passed: safe errors, pending retry, retained snapshot and GET-only recovery at desktop/tablet/phone sizes.',
    );
  } finally {
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
