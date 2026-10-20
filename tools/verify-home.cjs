const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verify() {
  const url = process.env.TEST_URL;
  assert(
    url && /^http:\/\/127\.0\.0\.1:(3107|3108)$/.test(url),
    'Use the isolated fixture server on port 3107 or 3108.',
  );
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
  await page.addInitScript(() => {
    const draw = CanvasRenderingContext2D.prototype.drawImage;
    CanvasRenderingContext2D.prototype.drawImage = function (...args) {
      if (this.canvas.matches('canvas[aria-label^="Research team office"]'))
        window.officeDraws = (window.officeDraws || 0) + 1;
      return Reflect.apply(draw, this, args);
    };
  });
  const errors = [];
  const writes = [];
  const apiPaths = new Set();
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    if (request.url().includes('/api/')) {
      apiPaths.add(new URL(request.url()).pathname);
      if (request.method() !== 'GET') writes.push(request.method());
    }
  });
  let failure = false;
  let reads = 0;
  const now = new Date();
  const at = now.toISOString();
  const run = {
    id: '218d734e-0446-492d-8b62-ffbd106d044e',
    accountKey: 'fixture-account-a',
    objective: 'Synthetic office state; no real account data.',
    status: 'running',
    createdAt: at,
    updatedAt: at,
    error: null,
    steps: [
      {
        role: 'intraday',
        status: 'running',
        output:
          'Fixture A: checking evidence. <script>window.unsafe=true</script>',
        error: null,
      },
      { role: 'reviewer', status: 'queued', output: null, error: null },
    ],
  };
  const company = {
    enabled: true,
    status: 'running',
    nextRunAt: new Date(now.getTime() + 3600000).toISOString(),
    lastRunId: run.id,
    error: null,
    activeRole: 'intraday',
  };
  let overview = { configured: true, latestRun: run, company };
  let runs = [run];
  await page.route('**/api/**', (route) =>
    route.fulfill({ status: 404, json: { error: 'Unmocked fixture API' } }),
  );
  await page.route('**/api/team', (route) => {
    reads++;
    return route.fulfill({
      status: failure ? 503 : 200,
      json: failure ? { error: 'Fixture connection unavailable' } : overview,
    });
  });
  await page.route('**/api/team/brief?*', (route) =>
    route.fulfill({
      json: {
        date: new URL(route.request().url()).searchParams.get('date'),
        runs,
      },
    }),
  );
  const team = page.getByRole('region', { name: 'Team', exact: true });
  const refresh = async () => {
    const response = page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/team',
    );
    await page
      .getByRole('button', { name: 'Refresh status', exact: true })
      .click();
    await response;
  };
  try {
    await fs.mkdir('.impeccable/review', { recursive: true });
    await page.goto(url);
    await page.getByText('Connected', { exact: true }).waitFor();
    await page.locator('canvas[data-ready="true"]').waitFor();
    assert.equal(await team.getByRole('button').count(), 8);
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .focus();
    await page.keyboard.press('Enter');
    await page
      .getByRole('heading', { name: 'Intraday evidence', exact: true })
      .waitFor();
    await page.getByText(run.steps[0].output, { exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.unsafe), undefined);
    assert(
      await page.locator('canvas').evaluate((canvas) => {
        const pixels = canvas
          .getContext('2d')
          .getImageData(0, 0, canvas.width, canvas.height).data;
        return pixels.some((value, index) => index % 4 !== 3 && value !== 0);
      }),
      'The actual vendored renderer must draw the office.',
    );
    await page
      .getByRole('button', { name: 'Pause animation', exact: true })
      .click();
    await page
      .getByRole('button', { name: 'Resume animation', exact: true })
      .waitFor();
    const stableDraws = async () => {
      await page.waitForTimeout(100);
      const before = await page.evaluate(() => window.officeDraws);
      await page.waitForTimeout(200);
      assert.equal(
        await page.evaluate(() => window.officeDraws),
        before,
        'Paused, reduced-motion, hidden or offscreen office must not keep rendering.',
      );
    };
    await stableDraws();
    await page
      .getByRole('button', { name: 'Resume animation', exact: true })
      .click();
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await stableDraws();
    await page.emulateMedia({ reducedMotion: 'no-preference' });
    await page
      .getByRole('button', { name: 'Pause animation', exact: true })
      .click();
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth,
        ),
        `${name} must fit the viewport.`,
      );
      if (process.env.CAPTURE_HOME !== '0')
        await page.screenshot({
          path: `.impeccable/review/home-${name}.png`,
          fullPage: true,
          animations: 'disabled',
        });
    }
    await page
      .getByRole('button', { name: 'Resume animation', exact: true })
      .click();
    await page
      .getByRole('heading', { name: 'Intraday evidence', exact: true })
      .scrollIntoViewIfNeeded();
    await stableDraws();
    await page.locator('canvas').scrollIntoViewIfNeeded();
    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', {
        configurable: true,
        value: true,
      });
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await stableDraws();
    const hiddenReads = reads;
    await page.waitForTimeout(10200);
    assert.equal(reads, hiddenReads, 'Hidden Home must not poll.');
    await page.evaluate(() => {
      delete document.hidden;
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/team',
    );
    await page.getByText('Connected', { exact: true }).waitFor();
    failure = true;
    await refresh();
    await page.getByText('Stale / unavailable', { exact: true }).waitFor();
    await team
      .getByRole('button', { name: 'Intraday Stale', exact: true })
      .waitFor();
    assert(
      await page.getByText(run.steps[0].output, { exact: true }).isVisible(),
    );
    failure = false;
    await refresh();
    await page.getByText('Connected', { exact: true }).waitFor();
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .waitFor();
    const other = {
      ...run,
      id: '318d734e-0446-492d-8b62-ffbd106d044e',
      accountKey: 'fixture-account-b',
      createdAt: new Date(now.getTime() - 3600000).toISOString(),
      updatedAt: new Date(now.getTime() - 3500000).toISOString(),
      status: 'completed',
      steps: run.steps.map((step) => ({
        ...step,
        status: 'completed',
        output: 'Fixture B recorded evidence.',
      })),
    };
    overview = {
      ...overview,
      latestRun: other,
      company: { ...company, status: 'waiting', activeRole: null },
    };
    runs = [run, other];
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Completed', exact: true })
      .waitFor();
    assert.equal(
      await page.getByText(run.steps[0].output, { exact: true }).count(),
      0,
      'New account clears prior evidence.',
    );
    await page
      .getByText('Fixture B recorded evidence.', { exact: true })
      .waitFor();
    overview = { ...overview, latestRun: null };
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Idle', exact: true })
      .waitFor();
    assert.equal(
      await page
        .getByText('Fixture B recorded evidence.', { exact: true })
        .count(),
      0,
    );
    overview = {
      configured: true,
      latestRun: other,
      company: {
        ...company,
        enabled: false,
        status: 'stopped',
        activeRole: null,
      },
    };
    runs = [other];
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Completed', exact: true })
      .waitFor();
    assert.deepEqual(writes, [], 'Home only reads APIs.');
    assert.deepEqual([...apiPaths].sort(), ['/api/team', '/api/team/brief']);
    const report = page.getByRole('link', {
      name: 'Open recorded report in Daily Brief',
      exact: true,
    });
    assert(
      (await report.getAttribute('href')).includes(
        `view=team-chat#run-${other.id}`,
      ),
    );
    await report.click();
    await page.getByRole('tab', { name: 'Team chat', exact: true }).waitFor();
    assert.equal(
      await page
        .getByRole('tab', { name: 'Team chat', exact: true })
        .getAttribute('aria-selected'),
      'true',
    );
    await page.locator(`#run-${other.id}`).waitFor();
    const day = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Bangkok',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date(other.createdAt));
    assert.equal(
      await page.getByLabel('Date', { exact: true }).inputValue(),
      day,
    );
    const unmountedReads = reads;
    await page.waitForTimeout(10200);
    assert.equal(
      reads,
      unmountedReads,
      'Unmounted Home must not leave a polling interval.',
    );
    await page.route('**/vendor/pixel-agents/renderer.js', (route) =>
      route.abort(),
    );
    await page.goto(url);
    await page
      .getByText(
        'Office illustration unavailable. Recorded team status is still available below.',
        { exact: true },
      )
      .waitFor();
    await team
      .getByRole('button', { name: 'Intraday Completed', exact: true })
      .waitFor();
    await page.emulateMedia({ reducedMotion: 'reduce' });
    assert.deepEqual(errors, [], 'No unhandled browser errors.');
    console.log(
      'Home fixtures passed: real renderer, eight stable roles, keyboard selection, read-only APIs, bounded plain text, offline/recovery, account transition, report deep link, renderer fallback and responsive layouts.',
    );
  } finally {
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
