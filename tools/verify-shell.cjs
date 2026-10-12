const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verifyShell() {
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await fs.mkdir('.impeccable/review', { recursive: true });
  try {
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.goto(process.env.TEST_URL || 'http://127.0.0.1:3000');
      await page.getByRole('heading', { name: 'Home', exact: true }).waitFor();
      const menu = page.getByRole('button', { name: 'Open menu' });
      const navigation = page.getByRole('navigation', {
        name: 'Main navigation',
      });
      if (width >= 768) {
        await navigation.waitFor();
        assert.equal(
          await page
            .locator('.MuiDrawer-paper')
            .evaluate((element) => element.scrollWidth <= element.clientWidth),
          true,
          'Navigation must fit inside the drawer, including its border',
        );
        assert.equal(await menu.count(), 0);
        assert.equal((await page.getByRole('main').boundingBox()).x, 240);
      } else {
        await menu.waitFor();
        assert.equal(await navigation.isVisible(), false);
        await menu.focus();
        await page.keyboard.press('Enter');
        await navigation.waitFor();
        assert.equal(
          await page
            .locator('.MuiDrawer-paper')
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
          .locator('.MuiBackdrop-root')
          .click({ position: { x: 350, y: 400 } });
        await navigation.waitFor({ state: 'hidden' });
      }
      await page.evaluate(() => {
        window.__navigationMarker = 'same-document';
      });
      if (width < 768) await menu.click();
      await page.getByRole('link', { name: 'Crypto', exact: true }).click();
      await page
        .getByRole('heading', { name: 'Crypto', exact: true })
        .waitFor();
      assert.equal(new URL(page.url()).pathname, '/crypto');
      assert.equal(
        await page.evaluate(() => window.__navigationMarker),
        'same-document',
      );
      if (width < 768) await menu.click();
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
      assert.equal(
        await page.evaluate(() => window.__navigationMarker),
        'same-document',
      );
      if (width < 768) await navigation.waitFor({ state: 'hidden' });
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
      );
      await page.screenshot({ path: `.impeccable/review/${name}.png` });
      console.log(`${name}: passed`);
    }
    await page.goto(
      new URL('/crypto', process.env.TEST_URL || 'http://127.0.0.1:3000').href,
    );
    await page.getByRole('heading', { name: 'Crypto', exact: true }).waitFor();
    await page.reload();
    await page.getByRole('heading', { name: 'Crypto', exact: true }).waitFor();
    await page.setViewportSize({ width: 320, height: 568 });
    await page.getByRole('button', { name: 'Open menu' }).waitFor();
    assert.equal(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
      true,
    );
    assert.deepEqual(errors, []);
    console.log('320px overflow and browser errors: passed');
  } finally {
    await browser.close();
  }
}
verifyShell().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
