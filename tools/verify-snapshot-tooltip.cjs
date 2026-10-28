const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');

async function verify() {
  const url = process.env.TEST_URL;
  assert.equal(url, 'http://127.0.0.1:3108', 'Isolated static fixtures only.');
  const directory =
    process.env.SNAPSHOT_CAPTURE_DIR || '.impeccable/review/snapshot-tooltip';
  await fs.mkdir(directory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const failures = [];
  const violations = [];
  const check = async (name, action) => {
    try {
      await action();
    } catch (error) {
      failures.push(name + ': ' + error.message);
    }
  };
  try {
    for (const [device, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 768, 1024],
      ['phone', 390, 844],
    ]) {
      const context = await browser.newContext({
        viewport: { width, height },
        hasTouch: device !== 'desktop',
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
      });
      await context.route('**/*', async (route) => {
        try {
          const request = route.request();
          const address = new URL(request.url());
          assert.equal(address.origin, url);
          assert.equal(
            request.method(),
            'GET',
            'Status details never send commands.',
          );
          if (address.pathname.startsWith('/api/')) {
            const body = structuredClone(fixture.data[address.pathname]);
            assert(body, 'Explicit API fixture required: ' + address.pathname);
            if (address.pathname === '/api/team/brief')
              body.date = address.searchParams.get('date');
            await route.fulfill({ json: body });
          } else {
            await staticBuildAsset(route, address);
          }
        } catch (error) {
          violations.push(error.message);
          await route.abort('blockedbyclient');
        }
      });
      await context.routeWebSocket('**/*', (socket) => {
        violations.push('Unexpected WebSocket');
        socket.close();
      });
      const page = await context.newPage();
      page.setDefaultTimeout(4000);
      page.on('pageerror', (error) => violations.push(error.message));
      await page.clock.install({ time: new Date(fixture.at) });
      await page.goto(url + '/crypto');
      const main = page.getByRole('main');
      const button = main.getByRole('button', {
        name: 'Snapshot status: Current',
        exact: true,
      });
      const tooltip = page.getByRole('tooltip');
      await button.waitFor();
      await page.clock.pauseAt(
        await page.evaluate(() => new Date(Date.now() + 100)),
      );
      await button.click();
      await tooltip.waitFor();
      await page.keyboard.press('Escape');
      await page.clock.runFor(350);
      await check(device + ' Escape cancels initial hover', async () =>
        assert.equal(
          await tooltip.count(),
          0,
          'A pending hover must not reopen click-dismissed details.',
        ),
      );
      await main.getByRole('heading', { name: 'Crypto', exact: true }).click();
      await page.mouse.move(0, 0);
      await page.clock.runFor(1000);
      await page.keyboard.press('Tab');
      await button.focus();
      await page.clock.runFor(350);
      await tooltip.waitFor();
      await check(device + ' accessible name', async () => {
        assert.equal(
          await button.count(),
          1,
          'Details describe the status button without replacing its name.',
        );
        const reference = await button.getAttribute('aria-describedby');
        assert(reference, 'The tooltip is an accessible description.');
        assert.equal(reference, await tooltip.getAttribute('id'));
      });
      await page.keyboard.press('Escape');
      await page.clock.runFor(350);
      await check(device + ' Escape', async () =>
        assert.equal(await tooltip.count(), 0),
      );
      await button.click();
      await tooltip.waitFor();
      await page.keyboard.press('Escape');
      await page.clock.runFor(350);
      await check(device + ' Escape cancels pending hover', async () =>
        assert.equal(
          await tooltip.count(),
          0,
          'A delayed hover must not reopen dismissed details.',
        ),
      );
      await main.getByRole('heading', { name: 'Crypto', exact: true }).click();
      await page.mouse.move(0, 0);
      await button.hover();
      await page.clock.runFor(350);
      await tooltip.waitFor();
      const bounds = await tooltip.boundingBox();
      assert(bounds);
      await page.mouse.move(
        bounds.x + bounds.width / 2,
        bounds.y + bounds.height / 2,
      );
      await page.clock.runFor(350);
      await check(device + ' hoverable details', async () => {
        assert.equal(
          await tooltip.isVisible(),
          true,
          'Pointer can move from the button onto its details.',
        );
      });
      await page.mouse.move(0, 0);
      await page.clock.runFor(350);
      if (device === 'desktop') await button.click();
      else await button.tap();
      await page.clock.runFor(350);
      await check(device + ' click or tap opens', async () => {
        await tooltip.waitFor();
      });
      if (await tooltip.isVisible())
        await check(device + ' bounds and content', async () => {
          assert.match(
            await tooltip.textContent(),
            /Snapshot:.*Prices:.*History:/s,
          );
          const box = await tooltip.boundingBox();
          assert(box && box.x >= 0 && box.x + box.width <= width + 1);
          assert.equal(
            await page.evaluate(
              () => document.documentElement.scrollWidth <= innerWidth,
            ),
            true,
          );
        });
      await page.screenshot({
        path: path.join(directory, device + '.png'),
        fullPage: true,
      });
      if (device === 'desktop')
        await main
          .getByRole('heading', { name: 'Crypto', exact: true })
          .click();
      else
        await main.getByRole('heading', { name: 'Crypto', exact: true }).tap();
      await page.clock.runFor(350);
      await check(device + ' outside click closes', async () =>
        assert.equal(await tooltip.count(), 0),
      );
      await context.close();
    }
    assert.deepEqual(
      violations,
      [],
      'No unmocked network, writes or page errors.',
    );
    assert.deepEqual(failures, []);
    console.log(
      'Snapshot details: name/description, hover, Escape, tap/click, outside dismissal and bounds passed on desktop/tablet/phone; static fixtures only.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
