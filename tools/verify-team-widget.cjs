const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verify() {
  const url = process.env.TEST_URL;
  assert(
    url && /^http:\/\/127\.0\.0\.1:(3107|3108)$/.test(url),
    'Use the isolated fixture server.',
  );
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
  });
  const errors = [];
  const writes = [];
  const reads = { overview: 0, brief: 0 };
  let account = 'a';
  let failBrief = false;
  let hangBrief = false;
  const date = '2026-10-20';
  const runs = (scope) =>
    Array.from({ length: scope === 'a' ? 12 : 1 }, (_, index) => ({
      id: `00000000-0000-4000-8000-${String(index + (scope === 'a' ? 1 : 101)).padStart(12, '0')}`,
      accountKey: `fixture-account-${scope}`,
      objective: `Synthetic account ${scope.toUpperCase()} report ${String(index + 1)}`,
      status: 'completed',
      createdAt: `${date}T01:${String(index).padStart(2, '0')}:00Z`,
      updatedAt: `${date}T01:${String(index).padStart(2, '0')}:01Z`,
      error: null,
      steps: ['ceo', 'analyst', 'reviewer'].map((role) => ({
        role,
        status: 'completed',
        output: `Fixture ${scope.toUpperCase()} ${role} report ${String(index + 1)}. ${'Recorded synthetic evidence. '.repeat(10)}`,
        error: null,
      })),
    }));
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    if (request.url().includes('/api/') && request.method() !== 'GET')
      writes.push(request.method());
  });
  await page.route('**/api/**', (route) =>
    route.fulfill({ status: 404, json: { error: 'Unmocked fixture API' } }),
  );
  await page.route('**/api/team', (route) => {
    reads.overview++;
    return route.fulfill({
      json: {
        configured: true,
        latestRun: runs(account).at(-1),
        company: {
          enabled: true,
          status: 'waiting',
          nextRunAt: null,
          lastRunId: null,
          error: null,
        },
      },
    });
  });
  await page.route('**/api/team/brief?*', (route) => {
    reads.brief++;
    if (hangBrief) return;
    return route.fulfill({
      status: failBrief ? 503 : 200,
      json: failBrief
        ? { error: 'Fixture reports unavailable' }
        : {
            date: new URL(route.request().url()).searchParams.get('date'),
            runs: runs(account),
          },
    });
  });
  const dialog = page.getByRole('dialog', { name: 'Team chat', exact: true });
  const open = async () => {
    await page.getByRole('button', { name: 'Team chat', exact: true }).click();
    await page.clock.runFor(350);
    await dialog.waitFor();
  };
  const close = async () => {
    await dialog.getByRole('button', { name: 'Close team chat' }).click();
    await page.clock.runFor(350);
    await dialog.waitFor({ state: 'hidden' });
  };
  try {
    await fs.mkdir('.impeccable/review', { recursive: true });
    await page.clock.install({ time: new Date(`${date}T03:00:00Z`) });
    await page.goto(url);
    await page
      .getByRole('button', { name: 'Team chat', exact: true })
      .waitFor();
    await page.getByText('Connected', { exact: true }).waitFor();
    const before = await page.locator('main').boundingBox();
    await open();
    await dialog
      .getByText(/Fixture A reviewer report 12\./)
      .first()
      .waitFor();
    assert(
      await dialog
        .getByRole('button', { name: 'Close team chat' })
        .evaluate((element) => element === document.activeElement),
      'Opening focuses the close control.',
    );
    const after = await page.locator('main').boundingBox();
    assert.equal(after.x, before.x, 'Overlay does not move the page.');
    assert.equal(
      after.width,
      before.width,
      'Overlay does not resize the page.',
    );
    assert(
      await page.evaluate(() =>
        Boolean(
          document.elementFromPoint(50, 150)?.closest('.MuiBackdrop-root'),
        ),
      ),
      'Backdrop blocks the page behind the overlay.',
    );
    await page.keyboard.press('Shift+Tab');
    assert(
      await dialog.evaluate((element) =>
        element.contains(document.activeElement),
      ),
      'Focus stays in the dialog.',
    );
    const conversation = dialog.getByRole('region', {
      name: 'Daily conversation',
    });
    await conversation.evaluate((element) => {
      element.scrollTop = 200;
    });
    await page.clock.runFor(50);
    await page.screenshot({
      path: '.impeccable/review/team-widget-desktop.png',
    });
    const position = await conversation.evaluate(
      (element) => element.scrollTop,
    );
    assert(position > 0);
    await page.keyboard.press('Escape');
    await page.clock.runFor(350);
    await dialog.waitFor({ state: 'hidden' });
    assert(
      await page
        .getByRole('button', { name: 'Team chat', exact: true })
        .evaluate((element) => element === document.activeElement),
      'Escape returns focus to the trigger.',
    );
    await page.getByRole('link', { name: 'Daily Brief', exact: true }).click();
    await page
      .getByRole('heading', { name: 'Daily Brief', exact: true })
      .waitFor();
    await open();
    assert.equal(
      await conversation.evaluate((element) => element.scrollTop),
      position,
      'Navigation preserves the widget reading position.',
    );
    const prior = { ...reads };
    await page.clock.runFor(10050);
    assert.equal(
      reads.overview - prior.overview,
      1,
      'Current-day page and widget share one overview poll.',
    );
    assert.equal(
      reads.brief - prior.brief,
      1,
      'Current-day page and widget share one brief poll.',
    );
    await page.goBack();
    await page.clock.runFor(350);
    await dialog.waitFor({ state: 'hidden' });
    assert(
      new URL(page.url()).pathname === '/daily-brief',
      'Back closes the overlay before leaving the route.',
    );
    await open();
    account = 'b';
    failBrief = true;
    await dialog.getByRole('button', { name: 'Refresh team chat' }).click();
    await dialog.getByText('Fixture reports unavailable').waitFor();
    assert.equal(
      await dialog.getByText(/Fixture A .* report/).count(),
      0,
      'A failed new-account brief cannot retain the previous account transcript.',
    );
    failBrief = false;
    await dialog.getByRole('button', { name: 'Refresh team chat' }).click();
    await dialog
      .getByText(/Fixture B reviewer report 1\./)
      .first()
      .waitFor();
    assert.equal(await dialog.getByText(/Fixture A .* report/).count(), 0);
    account = 'a';
    await dialog.getByRole('button', { name: 'Refresh team chat' }).click();
    await dialog
      .getByText(/Fixture A reviewer report 12\./)
      .first()
      .waitFor();
    account = 'b';
    hangBrief = true;
    await dialog.getByRole('button', { name: 'Refresh team chat' }).click();
    await page.clock.runFor(8100);
    await dialog.getByText('Research status request timed out.').waitFor();
    assert.equal(
      await dialog.getByText(/Fixture A .* report/).count(),
      0,
      'A new-account overview clears old evidence even when the paired brief times out.',
    );
    hangBrief = false;
    await dialog.getByRole('button', { name: 'Refresh team chat' }).click();
    await dialog
      .getByText(/Fixture B reviewer report 1\./)
      .first()
      .waitFor();
    await close();
    await page.setViewportSize({ width: 390, height: 844 });
    await open();
    const mobile = await dialog.boundingBox();
    assert.equal(mobile.x, 0);
    assert.equal(mobile.width, 390);
    assert.equal(mobile.height, 844);
    await page.screenshot({
      path: '.impeccable/review/team-widget-mobile.png',
    });
    await close();
    assert.deepEqual(writes, [], 'Report inspection sends no commands.');
    assert.deepEqual(errors, [], 'No unhandled browser errors.');
    console.log(
      'Team widget: desktop/mobile overlay, blocked background, focus trap/return, Escape/Back, route reading retention, one current-day poll, account-switch failure isolation, and read-only API fixtures passed.',
    );
  } finally {
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
