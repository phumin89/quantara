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
  const page = await browser.newPage();
  const main = page.getByRole('main');
  const chooseDay = async (day) => {
    await main.getByRole('button', { name: /choose date/i }).click();
    await page.clock.runFor(300);
    await page.getByRole('gridcell', { name: day, exact: true }).click();
    await page.clock.runFor(300);
  };
  await page.route('**/api/**', (route) =>
    route.fulfill({ status: 404, json: { error: 'Unmocked fixture API' } }),
  );
  await page.route('**/api/crypto/orders', (route) =>
    route.fulfill({ json: { proposals: [] } }),
  );
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  let failure = false;
  let reads = 0;
  const dayReads = new Map();
  let active = false;
  let writes = 0;
  let companyEnabled = false;
  const company = () => ({
    enabled: companyEnabled,
    status: companyEnabled ? (active ? 'running' : 'waiting') : 'stopped',
    nextRunAt: companyEnabled ? '2026-10-13T04:00:00Z' : null,
    lastRunId: null,
    lastCompletedAt: '2026-10-13T02:55:00Z',
    activeRole: active ? 'positions' : null,
    nextRole: active ? 'reviewer' : 'intraday',
    budgetReason: 'ordinary_research_limit',
    budgetMode: 'boundedRequests',
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
      output:
        role === 'analyst'
          ? 'Fixture analyst: evidence incomplete.\nReason: cost basis unknown.\nNext: verify retained history.\n\nFull calculations and limitations: <script>unsafe()</script>\nTask A1: Pending; missing historical fills.\nPreserve exact amounts and uncertainty.'
          : `Fixture ${role} report. <script>unsafe()</script>`,
      error: null,
    })),
    error: null,
    lesson: {
      lesson: 'Fixture lesson: keep a fixed horizon.',
      evidence: 'Fixture evidence: unknown realized profit.',
      nextTest: 'Fixture next test: compare the unchanged baseline.',
    },
  };
  const previousRun = {
    ...run,
    id: '118d734e-0446-492d-8b62-ffbd106d044e',
    objective: 'Fixture: previous portfolio evidence',
    createdAt: '2026-10-12T17:00:00+00:00',
    updatedAt: '2026-10-12T17:03:00+00:00',
    steps: run.steps.map((step) =>
      step.role === 'reviewer'
        ? { ...step, output: 'Fixture previous Reviewer summary.' }
        : step,
    ),
  };
  const modernRun = {
    ...run,
    id: '318d734e-0446-492d-8b62-ffbd106d044e',
    status: 'running',
    steps: ['positions', 'reviewer'].map((role) => ({
      role,
      status: 'running',
      output: `Fixture ${role} report.`,
      error: null,
    })),
  };
  page.on('request', (request) => {
    if (request.method() !== 'GET') writes++;
  });
  await page.route('**/api/team', (route) =>
    route.fulfill({
      json: {
        configured: true,
        latestRun: active ? modernRun : run,
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
    dayReads.set(date, (dayReads.get(date) || 0) + 1);
    await route.fulfill({
      status: failure ? 503 : 200,
      json: failure
        ? { error: 'Fixture storage failure' }
        : { date, runs: date === '2026-10-13' ? [previousRun, run] : [] },
    });
  });
  try {
    await fs.mkdir('.impeccable/review', { recursive: true });
    await page.clock.install({ time: new Date('2026-10-13T03:00:00Z') });
    await page.goto(`${url}/daily-brief`);
    await page
      .getByRole('heading', { name: 'Daily Brief', exact: true })
      .waitFor();
    const date = main.locator('[data-date]');
    await page.waitForFunction(
      () =>
        document
          .querySelector('main [data-date]')
          ?.getAttribute('data-date') === '2026-10-13',
    );
    assert.equal(await date.getAttribute('data-date'), '2026-10-13');
    const tradingTab = page.getByRole('tab', {
      name: 'Trading status',
      exact: true,
    });
    const teamChatTab = page.getByRole('tab', {
      name: 'Team chat',
      exact: true,
    });
    assert.equal(await tradingTab.getAttribute('aria-selected'), 'true');
    assert.equal(await teamChatTab.getAttribute('aria-selected'), 'false');
    assert.equal(
      await main.getByRole('region', { name: 'Daily conversation' }).count(),
      0,
    );
    await teamChatTab.focus();
    await page.keyboard.press('Enter');
    await main.getByRole('region', { name: 'Daily conversation' }).waitFor();
    assert.equal(writes, 0, 'Switching views never starts work');
    const conversation = main.getByRole('region', {
      name: 'Daily conversation',
    });
    await conversation
      .getByText('Fixture ceo report. <script>unsafe()</script>', {
        exact: true,
      })
      .first()
      .waitFor();
    const learningNotes = conversation.getByText('Learning notes', {
      exact: true,
    });
    assert.equal(await learningNotes.count(), 2);
    const firstNotes = learningNotes.first().locator('..');
    assert.equal(
      await firstNotes
        .getByText(`Lesson: ${run.lesson.lesson}`, { exact: true })
        .isVisible(),
      false,
    );
    await learningNotes.first().click();
    await conversation
      .getByText(`Lesson: ${run.lesson.lesson}`, { exact: true })
      .first()
      .waitFor();
    await conversation
      .getByText(`Evidence: ${run.lesson.evidence}`, { exact: true })
      .first()
      .waitFor();
    await conversation
      .getByText(`Next test: ${run.lesson.nextTest}`, { exact: true })
      .first()
      .waitFor();
    assert.equal(
      await page.locator('script').filter({ hasText: 'unsafe()' }).count(),
      0,
    );
    await page.getByRole('heading', { name: 'Daily summary' }).waitFor();
    const researchBrief = conversation.locator('details').filter({
      hasText: run.objective,
    });
    assert.equal(await researchBrief.count(), 1);
    assert.equal(
      await conversation.getByText(run.objective, { exact: true }).isVisible(),
      false,
    );
    await researchBrief.locator('summary').click();
    await conversation.getByText(run.objective, { exact: true }).waitFor();
    assert.equal(
      await main.getByText('Previous summaries (1)', { exact: true }).count(),
      1,
    );
    const latestSummary = main.getByText('Latest Reviewer report', {
      exact: true,
    });
    await page
      .getByRole('tab', { name: 'Trading status', exact: true })
      .click();
    assert.equal(await latestSummary.isVisible(), false);
    await page.getByRole('tab', { name: 'Team chat', exact: true }).click();
    await latestSummary.waitFor();
    await main.getByText('Previous summaries (1)', { exact: true }).click();
    await main
      .getByText('Fixture previous Reviewer summary.', { exact: true })
      .first()
      .waitFor();
    assert.equal(
      await page
        .getByRole('button', { name: 'Start task', exact: true })
        .count(),
      0,
    );
    await chooseDay('12');
    await conversation.getByText('No activity', { exact: true }).waitFor();
    assert.equal(
      await conversation
        .getByText('Fixture ceo report.', { exact: false })
        .count(),
      0,
    );
    await main
      .getByText('No reviewer summary recorded for this day.')
      .waitFor();
    const pastReads = dayReads.get('2026-10-12');
    await page.clock.runFor(15000);
    assert.equal(
      dayReads.get('2026-10-12'),
      pastReads,
      'Historical selected days must not poll',
    );
    active = true;
    await chooseDay('13');
    await conversation
      .getByText('Fixture ceo report.', { exact: false })
      .first()
      .waitFor();
    await page.getByText('Last completed', { exact: false }).waitFor();
    await page
      .getByText('Current role: Position Reviewer', { exact: true })
      .waitFor();
    await page
      .getByText('Next eligible: Evidence Reviewer', { exact: true })
      .waitFor();
    await page
      .getByText('Limited research calls: Routine research slots are busy.', {
        exact: true,
      })
      .waitFor();
    assert.equal(
      await page.getByText('ordinary_research_limit', { exact: true }).count(),
      0,
    );
    const activeReads = reads;
    const polled = page.waitForResponse((response) =>
      response.url().includes('/api/team/brief?'),
    );
    await page.clock.runFor(10100);
    await polled;
    assert(reads > activeReads, 'Today follows an active task');
    active = false;
    const terminal = page.waitForResponse((response) =>
      response.url().includes('/api/team/brief?'),
    );
    await page.clock.runFor(10100);
    await terminal;
    await page.waitForFunction(
      () =>
        document
          .querySelector('main [aria-label="Daily conversation"]')
          .getAttribute('aria-busy') === 'false',
    );
    const terminalReads = reads;
    await page.clock.runFor(10000);
    assert.equal(
      reads - terminalReads,
      1,
      'Terminal research retains exactly one shared status poll',
    );
    for (const [name, width, height] of [
      ['desktop', 1440, 900],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
      ['small', 320, 640],
    ]) {
      await page.setViewportSize({ width, height });
      await page.clock.runFor(300);
      await page.waitForTimeout(100);
      assert(
        await page.evaluate(
          () =>
            document.documentElement.scrollWidth <=
            document.documentElement.clientWidth,
        ),
        `${name} overflow`,
      );
      const members = page.getByRole('button', { name: 'Members · 7' });
      if (width < 1024) {
        await members.click();
        await page
          .getByText('Direction & coordination', { exact: true })
          .waitFor();
        await page.getByText('Available', { exact: true }).first().waitFor();
        await members.click();
      }
      if (process.env.CAPTURE !== '0' && name !== 'small')
        await page.screenshot({
          path: `.impeccable/review/continuous-${name}.png`,
          fullPage: true,
        });
    }
    failure = true;
    await page.reload();
    await main.getByText('Fixture storage failure', { exact: true }).waitFor();
    await page.getByRole('tab', { name: 'Team chat', exact: true }).click();
    await conversation.getByText('Daily brief unavailable').waitFor();
    failure = false;
    const recovered = page.waitForResponse(
      (response) =>
        response.url().includes('/api/team/brief?') &&
        response.status() === 200,
    );
    await page.clock.runFor(30100);
    await recovered;
    await conversation
      .getByText('Fixture ceo report.', { exact: false })
      .first()
      .waitFor();
    await page.goto(`${url}/team`);
    await main.locator('[data-date="2026-10-13"]').waitFor();
    await page.getByRole('tab', { name: 'Team chat', exact: true }).click();
    await page
      .getByRole('heading', { name: 'Daily Brief', exact: true })
      .waitFor();
    assert.equal(writes, 0, 'Opening/changing days never starts AI work');
    await page
      .getByRole('button', { name: 'Resume research', exact: true })
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
      .getByRole('button', { name: 'Pause research', exact: true })
      .click();
    await page.getByText('AI research stopped', { exact: true }).waitFor();
    await page.reload();
    await main.locator('[data-date="2026-10-13"]').waitFor();
    await page.getByRole('tab', { name: 'Team chat', exact: true }).click();
    await page.getByText('AI research stopped', { exact: true }).waitFor();
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
