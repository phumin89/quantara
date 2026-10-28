const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verify() {
  const url = 'http://127.0.0.1:3110';
  assert.equal(
    process.env.PREVIEW_URL,
    url,
    'Only the task synthetic preview is allowed.',
  );
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
  });
  const errors = [],
    writes = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    if (request.url().includes('/api/') && request.method() !== 'GET')
      writes.push(request.method());
  });
  try {
    await page.goto(url);
    await page
      .getByRole('status')
      .getByText(
        'Synthetic preview · fixture data only · trading controls unavailable',
        { exact: true },
      )
      .waitFor();
    const app = page.frameLocator('iframe[title="Quantara synthetic preview"]');
    await app.locator('iframe[data-ready="true"]').waitFor();
    await app.getByRole('link', { name: 'Dashboard', exact: true }).click();
    await app
      .getByTestId('dashboard-day-net')
      .getByText('THB 12.25', { exact: true })
      .waitFor();
    await app
      .getByRole('complementary', { name: 'Team conversation', exact: true })
      .waitFor();
    await app
      .getByRole('button', { name: 'Collapse team chat', exact: true })
      .click();
    await app
      .getByRole('button', { name: 'Expand team chat', exact: true })
      .click();
    await app
      .getByRole('complementary', { name: 'Team conversation', exact: true })
      .waitFor();
    await app
      .getByRole('button', { name: 'Collapse team chat', exact: true })
      .click();
    await fs.mkdir('.impeccable/review', { recursive: true });
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['mobile', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page.screenshot({
        path: `.impeccable/review/synthetic-preview-${name}.png`,
        fullPage: true,
        animations: 'disabled',
      });
      assert(
        await page.getByRole('status').isVisible(),
        'Synthetic label remains visible across navigation and sizes.',
      );
    }
    const denied = await page.request.post(`${url}/health`);
    assert.equal(
      denied.status(),
      405,
      'Wrapper rejects writes without forwarding.',
    );
    assert.deepEqual(writes, []);
    assert.deepEqual(errors, []);
    console.log(
      'Synthetic preview: visible persistent label, nested original office, real HTTP Dashboard, global chat, desktop/mobile, no browser errors and GET-only passed.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
