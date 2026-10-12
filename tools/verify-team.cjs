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
  const id = '218d734e-0446-492d-8b62-ffbd106d044e';
  let configured = true;
  let latestRun = null;
  let reads = 0;
  let starts = 0;
  let failure = false;
  await page.route('**/api/team', async (route) => {
    reads++;
    await route.fulfill({
      json: failure
        ? { error: 'Fixture storage failure' }
        : { configured, latestRun },
      status: failure ? 503 : 200,
    });
  });
  await page.route('**/api/team/runs', async (route) => {
    starts++;
    latestRun = {
      id,
      objective: route.request().postDataJSON().objective,
      status: 'queued',
      createdAt: '2026-10-12T12:00:00+00:00',
      updatedAt: '2026-10-12T12:00:00+00:00',
      steps: ['ceo', 'analyst', 'reviewer'].map((role) => ({
        role,
        status: 'queued',
        output: null,
        error: null,
      })),
      error: null,
    };
    await route.fulfill({ status: 202, json: latestRun });
  });
  await page.route('**/api/team/stop?*', async (route) => {
    latestRun = {
      ...latestRun,
      status: 'cancelled',
      steps: latestRun.steps.map((step) => ({ ...step, status: 'cancelled' })),
    };
    await route.fulfill({ json: latestRun });
  });
  try {
    await page.clock.install();
    await page.goto(`${url}/team`);
    await page.getByText('Give the team a goal to begin.').waitFor();
    const objective = page.getByRole('textbox', {
      name: 'Objective',
      exact: true,
    });
    const start = page.getByRole('button', { name: 'Start task', exact: true });
    assert(await start.isDisabled());
    await objective.fill('Review the portfolio with evidence');
    const startedResponse = page.waitForResponse((response) =>
      response.url().endsWith('/api/team/runs'),
    );
    await start.click();
    await page.getByRole('button', { name: 'Stop', exact: true }).waitFor();
    await startedResponse;
    assert.equal(starts, 1);
    assert(await start.isDisabled());
    for (let i = 0; i < 3; i++) {
      const response = page.waitForResponse((value) =>
        value.url().endsWith('/api/team'),
      );
      await page.clock.runFor(2100);
      await response;
    }
    assert.equal(reads, 4, 'Unchanged queued status must continue polling');
    await page.getByRole('button', { name: 'Stop', exact: true }).click();
    await page.getByText('cancelled', { exact: true }).first().waitFor();
    const stoppedReads = reads;
    await page.clock.runFor(10000);
    assert.equal(reads, stoppedReads, 'Terminal task must stop polling');
    assert(
      await page
        .getByRole('button', { name: 'Stop', exact: true })
        .isDisabled(),
    );
    latestRun = {
      ...latestRun,
      status: 'completed',
      steps: latestRun.steps.map((step) => ({
        ...step,
        status: 'completed',
        output: `Fixture ${step.role} report. <script>unsafe()</script>`,
      })),
    };
    await fs.mkdir('.impeccable/review', { recursive: true });
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
      ['small', 320, 740],
    ]) {
      await page.setViewportSize({ width, height });
      await page.reload();
      await page
        .getByText('Fixture reviewer report.', { exact: false })
        .waitFor();
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
        `${name} overflow`,
      );
      assert.equal(
        await page.locator('script').filter({ hasText: 'unsafe()' }).count(),
        0,
        'Reports render plain text',
      );
      if (name !== 'small')
        await page.screenshot({
          path: `.impeccable/review/team-${name}.png`,
          fullPage: true,
        });
    }
    configured = false;
    latestRun = null;
    await page.reload();
    await page
      .getByText('Configure the Codex worker on the server to start tasks.')
      .waitFor();
    await objective.fill('Should stay disabled');
    assert(await start.isDisabled());
    failure = true;
    await page.reload();
    await page.getByText('Fixture storage failure').waitFor();
    assert(await start.isDisabled());
    failure = false;
    await page.getByRole('button', { name: 'Retry', exact: true }).click();
    await page.getByText('Give the team a goal to begin.').waitFor();
    const api = await browser.newContext();
    assert.equal(
      (
        await api.request.get(`${url}/api/team`, {
          headers: { Host: 'attacker.invalid' },
        })
      ).status(),
      403,
      'DNS rebinding must not expose team reports',
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
      'AI team UI: real composition, start/stop, unchanged-state polling, terminal stop, safe reports, error/retry, configuration gate, responsive layouts and proxy validation passed. Isolated fixtures only.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
