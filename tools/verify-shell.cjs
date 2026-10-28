const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function verifyShell() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  assert.match(url, /^http:\/\/127\.0\.0\.1:(3000|3107|3108)$/);
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  await fs.mkdir('.impeccable/review', { recursive: true });
  try {
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['small-desktop', 1024, 1024],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
      ['narrow', 320, 568],
    ]) {
      const context = await browser.newContext({
        viewport: { width, height },
        serviceWorkers: 'block',
        reducedMotion: 'reduce',
      });
      const page = await context.newPage();
      page.setDefaultTimeout(10000);
      const writes = [],
        unmocked = [],
        escaped = [],
        errors = [];
      await context.route('**/*', (route) => {
        const request = route.request();
        const address = new URL(request.url());
        if (address.origin !== url) {
          escaped.push(address.origin);
          return route.abort();
        }
        if (!address.pathname.startsWith('/api/')) return route.continue();
        if (request.method() !== 'GET') {
          writes.push(address.pathname);
          return route.fulfill({
            status: 405,
            json: { error: 'Fixture writes forbidden' },
          });
        }
        const body = structuredClone(fixture.data[address.pathname]);
        if (address.pathname === '/api/team/brief' && body)
          body.date = address.searchParams.get('date');
        if (!body) unmocked.push(address.pathname);
        return route.fulfill(
          body
            ? { json: body }
            : { status: 404, json: { error: 'Fixture endpoint unavailable' } },
        );
      });
      page.on('pageerror', (error) => errors.push(error.message));
      await page.clock.install({ time: new Date(fixture.at) });
      await page.goto(url);
      await page.getByRole('heading', { name: 'Home', exact: true }).waitFor();
      await page.keyboard.press('Tab');
      assert.equal(
        await page
          .getByRole('link', { name: 'Skip to main content' })
          .evaluate((e) => e === document.activeElement),
        true,
      );
      await page.keyboard.press('Enter');
      assert.equal(
        await page
          .getByRole('main')
          .evaluate((e) => e === document.activeElement),
        true,
      );
      assert.equal(
        await page
          .locator('nav a')
          .evaluateAll((links) =>
            links.every((link) => link.parentElement?.tagName !== 'UL'),
          ),
        true,
      );
      const menu = page.getByRole('button', { name: 'Open menu' });
      const navigation = page.getByRole('navigation', {
        name: 'Main navigation',
      });
      if (width >= 1024) {
        await navigation.waitFor();
        assert.equal(
          await page
            .getByRole('banner')
            .evaluate((element) => element.scrollWidth <= element.clientWidth),
          true,
          'Navigation fits the slim header.',
        );
        assert.equal(await menu.count(), 0);
        const mainBounds = await page.getByRole('main').boundingBox();
        assert(mainBounds && mainBounds.width <= 1344 && mainBounds.x >= 0);
        assert(
          Math.abs(mainBounds.x * 2 + mainBounds.width - width) <= 1,
          'Desktop content stays centered within its maximum width.',
        );
        assert.equal(
          await page
            .locator('.MuiDrawer-paper')
            .filter({ has: navigation })
            .count(),
          0,
        );
      } else {
        await menu.waitFor();
        assert.equal(await navigation.isVisible(), false);
        await menu.focus();
        await page.keyboard.press('Enter');
        await navigation.waitFor();
        assert.equal(
          await page
            .locator('.MuiDrawer-paper')
            .filter({ has: navigation })
            .evaluate((element) => element.scrollWidth <= element.clientWidth),
          true,
          'Mobile navigation must fit inside the drawer',
        );
        await page.keyboard.press('Escape');
        await navigation.waitFor({ state: 'hidden' });
        assert.equal(
          await menu.evaluate((e) => e === document.activeElement),
          true,
        );
        await menu.click();
        await page.getByRole('link', { name: 'Home', exact: true }).click();
        await navigation.waitFor({ state: 'hidden' });
        await menu.click();
        await page
          .locator('.MuiModal-root')
          .filter({ has: navigation })
          .locator('.MuiBackdrop-root')
          .click({
            position: { x: width - 16, y: Math.min(400, height - 16) },
          });
        await navigation.waitFor({ state: 'hidden' });
      }
      await page.evaluate(() => {
        window.__navigationMarker = 'same-document';
      });
      if (width < 1024) await menu.click();
      await page.getByRole('link', { name: 'Crypto', exact: true }).click();
      await page
        .getByRole('heading', { name: 'Crypto', exact: true })
        .waitFor();
      assert.equal(new URL(page.url()).pathname, '/crypto');
      assert.equal(
        await page.evaluate(() => window.__navigationMarker),
        'same-document',
      );
      if (width < 1024) await menu.click();
      assert.equal(
        await page
          .getByRole('link', { name: 'Crypto', exact: true })
          .getAttribute('aria-current'),
        'page',
      );
      assert.equal(
        await page
          .getByRole('link', { name: 'Home', exact: true })
          .getAttribute('aria-current'),
        null,
      );
      await page.getByRole('link', { name: 'Home', exact: true }).click();
      await page.getByRole('heading', { name: 'Home', exact: true }).waitFor();
      assert.equal(new URL(page.url()).pathname, '/');
      await page.goBack();
      await page
        .getByRole('heading', { name: 'Crypto', exact: true })
        .waitFor();
      if (width < 1024) await menu.click();
      assert.equal(
        await page
          .getByRole('link', { name: 'Crypto', exact: true })
          .getAttribute('aria-current'),
        'page',
      );
      if (width < 1024) await page.keyboard.press('Escape');
      await page.goForward();
      await page.getByRole('heading', { name: 'Home', exact: true }).waitFor();
      if (width < 1024) await menu.click();
      assert.equal(
        await page
          .getByRole('link', { name: 'Home', exact: true })
          .getAttribute('aria-current'),
        'page',
      );
      if (width < 1024) await page.keyboard.press('Escape');
      assert.equal(
        await page.evaluate(() => window.__navigationMarker),
        'same-document',
      );
      if (width < 1024) await navigation.waitFor({ state: 'hidden' });
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
      );
      if (width === 320) {
        await page.goto(`${url}/crypto`);
        await page
          .getByRole('heading', { name: 'Crypto', exact: true })
          .waitFor();
        await page.reload();
        await page
          .getByRole('heading', { name: 'Crypto', exact: true })
          .waitFor();
        assert.equal(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
          ),
          true,
          'Direct loading and reloading Crypto fits a narrow viewport.',
        );
      }
      await page.screenshot({ path: `.impeccable/review/${name}.png` });
      assert.deepEqual(writes, []);
      assert.deepEqual(unmocked, []);
      assert.deepEqual(escaped, []);
      assert.deepEqual(errors, []);
      console.log(`${name}: passed`);
      await context.close();
    }
  } finally {
    await browser.close();
  }
}
verifyShell().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
