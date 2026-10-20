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
  const page = await browser.newPage({ reducedMotion: 'reduce' });
  const main = page.getByRole('main');
  const chooseDay = async (day) => {
    await main.getByRole('button', { name: /choose date/i }).click();
    await page.clock.runFor(300);
    await page.getByRole('gridcell', { name: day, exact: true }).click();
    await page.clock.runFor(300);
  };
  let writes = 0;
  let count = 12;
  const errors = [];
  const runs = (size, date = '2026-10-19') =>
    Array.from({ length: size }, (_, index) => ({
      id: `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
      objective: `Scroll fixture ${String(index + 1)}`,
      status: 'completed',
      createdAt: `${date}T01:00:00Z`,
      updatedAt: `${date}T01:01:00Z`,
      error: null,
      steps: ['ceo', 'analyst', 'reviewer'].map((role) => ({
        role,
        status: 'completed',
        output: `${role} · report ${String(index + 1)} · verified fixture evidence`,
        error: null,
      })),
    }));
  const refresh = async () => {
    const response = page.waitForResponse((item) =>
      item.url().includes('/api/team/brief?'),
    );
    await page.clock.runFor(10100);
    await response;
    await main
      .getByText(`Scroll fixture ${String(count)}`, { exact: true })
      .waitFor({ state: 'attached' });
  };
  const atBottom = async () => {
    await page.waitForFunction(
      () => {
        const element = document.querySelector(
          'main [aria-label="Daily conversation"]',
        );
        return (
          element &&
          element.scrollHeight - element.clientHeight - element.scrollTop <= 2
        );
      },
      null,
      { timeout: 2000 },
    );
  };
  page.on('pageerror', (error) => errors.push(error.message));
  await page.route('**/api/**', (route) => {
    if (route.request().method() !== 'GET') writes++;
    return route.fulfill({
      status: 404,
      json: { error: 'Unmocked fixture API' },
    });
  });
  await page.route('**/api/crypto/orders', (route) =>
    route.fulfill({ json: { proposals: [] } }),
  );
  await page.route('**/api/team', (route) =>
    route.fulfill({
      json: {
        configured: true,
        latestRun: { ...runs(1)[0], status: 'running' },
        company: {
          enabled: true,
          status: 'running',
          nextRunAt: null,
          lastRunId: null,
          error: null,
        },
      },
    }),
  );
  await page.route('**/api/team/brief?*', (route) => {
    const date = new URL(route.request().url()).searchParams.get('date');
    return route.fulfill({
      json: { date, runs: runs(date === '2026-10-18' ? 8 : count, date) },
    });
  });
  try {
    await page.clock.install({ time: new Date('2026-10-19T03:00:00Z') });
    await page.goto(`${url}/daily-brief`);
    await main.getByRole('button', { name: /choose date/i }).waitFor();
    const chat = page.getByRole('tab', { name: 'Team chat', exact: true });
    await chat.click();
    const conversation = main.getByRole('region', {
      name: 'Daily conversation',
    });
    await conversation
      .getByText('reviewer · report 12 · verified fixture evidence', {
        exact: true,
      })
      .waitFor();
    assert(
      await conversation.evaluate(
        (element) => element.scrollHeight > element.clientHeight,
      ),
      'Fixture must overflow',
    );
    await atBottom();
    const documentTop = await page.evaluate(() => window.scrollY);
    count++;
    await refresh();
    await atBottom();
    assert.equal(
      await page.evaluate(() => window.scrollY),
      documentTop,
      'Only the conversation scrolls',
    );

    await conversation.evaluate((element) => {
      element.scrollTop = 0;
    });
    await refresh();
    assert.equal(
      await conversation.evaluate((element) => element.scrollTop),
      0,
      'Unchanged polls preserve reading position',
    );
    count++;
    await refresh();
    assert.equal(
      await conversation.evaluate((element) => element.scrollTop),
      0,
      'New reports do not pull a reader away from older messages',
    );

    await conversation.evaluate((element) => {
      element.scrollTop = element.scrollHeight;
    });
    count++;
    await refresh();
    await atBottom();
    await page
      .getByRole('tab', { name: 'Trading status', exact: true })
      .click();
    count++;
    await refresh();
    await chat.click();
    await atBottom();
    await conversation.evaluate((element) => {
      element.scrollTop = 0;
    });
    await chooseDay('18');
    await conversation
      .getByText('reviewer · report 8 · verified fixture evidence', {
        exact: true,
      })
      .waitFor();
    await atBottom();

    await fs.mkdir('.impeccable/review', { recursive: true });
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['phone', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await page
        .getByRole('tab', { name: 'Trading status', exact: true })
        .click();
      await chat.click();
      await atBottom();
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth,
        ),
        `${name} overflow`,
      );
      if (process.env.CAPTURE !== '0')
        await page.screenshot({
          path: `.impeccable/review/chat-scroll-${name}.png`,
          fullPage: true,
        });
    }
    assert.equal(writes, 0, 'Chat following never sends commands');
    assert.deepEqual(errors, []);
    console.log(
      'Chat scrolling: initial load, new reports, manual reading, tab/date changes and three viewport sizes passed; isolated APIs only.',
    );
  } finally {
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
