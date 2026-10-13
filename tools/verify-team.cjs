const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');

async function verify() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  let failure = false;
  let reads = 0;
  let active = false;
  let writes = 0;
  let companyEnabled = false;
  const company = () => ({
    enabled: companyEnabled,
    status: companyEnabled ? (active ? 'running' : 'waiting') : 'stopped',
    nextRunAt: companyEnabled ? '2026-10-13T04:00:00Z' : null,
    lastRunId: null,
    error: null,
  });
  const run = {
    id: '218d734e-0446-492d-8b62-ffbd106d044e',
    objective: 'Fixture: review portfolio evidence',
    status: 'completed',
    createdAt: '2026-10-12T18:00:00+00:00',
    updatedAt: '2026-10-12T18:03:00+00:00',
    steps: ['ceo', 'analyst', 'reviewer'].map((role) => ({
      role,
      status: 'completed',
      output: `Fixture ${role} report. <script>unsafe()</script>`,
      error: null,
    })),
    error: null,
  };
  page.on('request', (request) => {
    if (request.method() !== 'GET') writes++;
  });
  await page.route('**/api/team', (route) =>
    route.fulfill({
      json: {
        configured: true,
        latestRun: active ? { ...run, status: 'running' } : run,
        company: company(),
      },
    }),
  );
  await page.route('**/api/team/company', async (route) => {
    companyEnabled = route.request().postDataJSON().enabled;
    await route.fulfill({ json: company() });
  });
  await page.route('**/api/team/brief?*', async (route) => {
    reads++;
    const date = new URL(route.request().url()).searchParams.get('date');
    await route.fulfill({
      status: failure ? 503 : 200,
      json: failure
        ? { error: 'Fixture storage failure' }
        : { date, runs: date === '2026-10-13' ? [run] : [] },
    });
  });
  try {
    await fs.mkdir('.impeccable/review', { recursive: true });
    await page.clock.install({ time: new Date('2026-10-13T03:00:00Z') });
    await page.goto(`${url}/daily-brief`);
    await page
      .getByRole('heading', { name: 'Daily Brief', exact: true })
      .waitFor();
    const date = page.getByLabel('Date', { exact: true });
    await page.waitForFunction(
      () =>
        document.querySelector('input[type="date"]')?.value === '2026-10-13',
    );
    assert.equal(await date.inputValue(), '2026-10-13');
    const conversation = page.getByRole('region', {
      name: 'Daily conversation',
    });
    await conversation
      .getByText('Fixture ceo report. <script>unsafe()</script>', {
        exact: true,
      })
      .waitFor();
    assert.equal(
      await page.locator('script').filter({ hasText: 'unsafe()' }).count(),
      0,
    );
    await page.getByRole('heading', { name: 'Daily summary' }).waitFor();
    assert.equal(
      await page
        .getByRole('button', { name: 'Start task', exact: true })
        .count(),
      0,
    );
    await date.fill('2026-10-12');
    await conversation.getByText('No activity', { exact: true }).waitFor();
    assert.equal(
      await conversation
        .getByText('Fixture ceo report.', { exact: false })
        .count(),
      0,
    );
    await page
      .getByText('No reviewer summary recorded for this day.')
      .waitFor();
    const pastReads = reads;
    await page.clock.runFor(15000);
    assert.equal(reads, pastReads, 'Historical days must not poll');
    active = true;
    await date.fill('2026-10-13');
    await conversation
      .getByText('Fixture ceo report.', { exact: false })
      .waitFor();
    const activeReads = reads;
    const polled = page.waitForResponse((response) =>
      response.url().includes('/api/team/brief?'),
    );
    await page.clock.runFor(5100);
    await polled;
    assert(reads > activeReads, 'Today follows an active task');
    active = false;
    const terminal = page.waitForResponse((response) =>
      response.url().includes('/api/team/brief?'),
    );
    await page.clock.runFor(5100);
    await terminal;
    await page.waitForFunction(
      () =>
        document
          .querySelector('[aria-label="Daily conversation"]')
          .getAttribute('aria-busy') === 'false',
    );
    const terminalReads = reads;
    await page.clock.runFor(15000);
    assert.equal(reads, terminalReads, 'Terminal work stops polling');
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
      ['small', 320, 640],
    ]) {
      await page.setViewportSize({ width, height });
      assert(
        await page.evaluate(
          () =>
            document.documentElement.scrollWidth <=
            document.documentElement.clientWidth,
        ),
        `${name} overflow`,
      );
      const members = page.getByRole('button', { name: 'Members · 3' });
      if (width < 1024) {
        await members.click();
        await page
          .getByText('Direction & coordination', { exact: true })
          .waitFor();
        await members.click();
      }
      if (name !== 'small')
        await page.screenshot({
          path: `.impeccable/review/daily-brief-${name}.png`,
          fullPage: true,
        });
    }
    failure = true;
    await page.reload();
    await page.getByText('Fixture storage failure', { exact: true }).waitFor();
    await conversation.getByText('Daily brief unavailable').waitFor();
    failure = false;
    await page.getByRole('button', { name: 'Retry', exact: true }).click();
    await conversation
      .getByText('Fixture ceo report.', { exact: false })
      .waitFor();
    await page.goto(`${url}/team`);
    await page
      .getByRole('heading', { name: 'Daily Brief', exact: true })
      .waitFor();
    assert.equal(writes, 0, 'Opening/changing days never starts AI work');
    await page
      .getByRole('button', { name: 'Resume company', exact: true })
      .click();
    await page.getByText('Company is monitoring', { exact: true }).waitFor();
    const monitoringReads = reads;
    const scheduled = page.waitForResponse((response) =>
      response.url().includes('/api/team/brief?'),
    );
    await page.clock.runFor(30100);
    await scheduled;
    assert(
      reads > monitoringReads,
      'Idle automatic company is monitored without a manual task',
    );
    await page
      .getByRole('button', { name: 'Stop company', exact: true })
      .click();
    await page.getByText('Company stopped', { exact: true }).waitFor();
    await page.reload();
    await page.getByText('Company stopped', { exact: true }).waitFor();
    assert.equal(writes, 2, 'Only explicit company controls send writes');
    const api = await browser.newContext();
    for (const badDate of [
      'invalid',
      '2026-02-30',
      '0000-01-01',
      '9999-12-31',
    ]) {
      assert.equal(
        (
          await api.request.get(`${url}/api/team/brief?date=${badDate}`)
        ).status(),
        400,
      );
    }
    assert.equal(
      (
        await api.request.get(`${url}/api/team/brief?date=2026-10-13`, {
          headers: { Host: 'attacker.invalid' },
        })
      ).status(),
      403,
    );
    assert.equal(
      (
        await api.request.post(`${url}/api/team/runs`, {
          headers: { Origin: 'https://attacker.invalid' },
          data: { objective: 'x' },
        })
      ).status(),
      403,
    );
    assert.equal(
      (
        await api.request.post(`${url}/api/team/company`, {
          data: { enabled: 'true' },
        })
      ).status(),
      400,
    );
    assert.equal(
      (
        await api.request.post(`${url}/api/team/company`, {
          headers: { Origin: 'https://attacker.invalid' },
          data: { enabled: true },
        })
      ).status(),
      403,
    );
    assert.equal(
      (
        await api.request.post(`${url}/api/team/runs`, {
          data: { objective: ' ' },
        })
      ).status(),
      400,
    );
    assert.equal(
      (
        await api.request.post(`${url}/api/team/stop?id=invalid`, { data: {} })
      ).status(),
      400,
    );
    await api.close();
    assert.deepEqual(errors, []);
    console.log(
      'Daily Brief: date filtering, plaintext reports, responsive members, work/idle polling, error/retry, persisted company controls, no unsolicited writes and proxy validation passed. Isolated fixtures only.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
