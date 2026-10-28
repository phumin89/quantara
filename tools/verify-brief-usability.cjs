const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

const origin = 'http://127.0.0.1:3107';
const root = path.resolve(__dirname, '..');
const directory = path.join(root, '.impeccable/review/brief-usability');
const historicalDate = '2026-10-19';
const reviewerText = fixture.run.steps.find(
  (step) => step.role === 'reviewer',
).output;

function report(id, createdAt, prefix) {
  const run = structuredClone(fixture.run);
  run.id = id;
  run.createdAt = createdAt;
  run.updatedAt = new Date(Date.parse(createdAt) + 60_000).toISOString();
  run.steps.forEach((step) => {
    step.output = `${prefix} ${step.role}: retained synthetic evidence.`;
    if (step.role === 'reviewer') {
      step.output = [
        prefix === 'Latest' ? reviewerText : step.output,
        'ข้อมูลจำลองสำหรับตรวจการอ่าน: ต้นทุนและกำไรที่ยังไม่มีหลักฐานต้องคงสถานะไม่ทราบ รายงานนี้ไม่มีอำนาจส่งคำสั่งซื้อขาย',
        ...Array.from(
          { length: 18 },
          (_, index) =>
            `${prefix} observation ${index + 1}: fixture records retain unknown costs and read-only research. No execution authority.`,
        ),
      ].join('\n\n');
    }
  });
  return run;
}

const latest = report(fixture.run.id, fixture.run.createdAt, 'Latest');
const older = report(
  '218d734e-0446-492d-8b62-ffbd106d044f',
  '2026-10-20T01:58:00Z',
  'Earlier',
);
const historical = report(
  '218d734e-0446-492d-8b62-ffbd106d0450',
  '2026-10-19T02:58:00Z',
  'Archive',
);

function deferred() {
  let resolve;
  const promise = new Promise((complete) => {
    resolve = complete;
  });
  return { promise, resolve };
}

async function received(promise, name) {
  let timer;
  try {
    await Promise.race([
      promise,
      new Promise((_, reject) => {
        timer = setTimeout(
          () => reject(new Error(`Missing synthetic ${name}.`)),
          10_000,
        );
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}

async function builtAsset(route, address) {
  const pathname = decodeURIComponent(address.pathname);
  let assetRoot;
  let relative;
  if (
    ['/', '/daily-brief', '/dashboard', '/crypto', '/team'].includes(pathname)
  ) {
    assetRoot = path.join(root, 'apps/web/.next/server/app');
    relative =
      (pathname === '/' ? 'index' : pathname.slice(1)) +
      (route.request().headers().rsc === '1' ? '.rsc' : '.html');
  } else if (pathname.startsWith('/_next/static/')) {
    assetRoot = path.join(root, 'apps/web/.next/static');
    relative = pathname.slice('/_next/static/'.length);
  } else {
    assetRoot = path.join(root, 'apps/web/public');
    relative = pathname.slice(1);
  }
  const file = path.resolve(assetRoot, relative);
  const withinRoot = path.relative(assetRoot, file);
  assert(
    withinRoot && !withinRoot.startsWith('..') && !path.isAbsolute(withinRoot),
  );
  await route.fulfill({
    path: file,
    ...(relative.endsWith('.rsc') ? { contentType: 'text/x-component' } : {}),
  });
}

async function pendingControl(page, control, writes, phase) {
  assert.equal(
    await control.isDisabled(),
    true,
    'Pending command remains disabled.',
  );
  assert.equal(
    await control.getAttribute('aria-busy'),
    'true',
    'Pending command exposes busy state.',
  );
  assert.equal(await control.isVisible(), true);
  assert.match(
    await control.innerText(),
    /Updating…/,
    'Pending label stays visible.',
  );
  assert.equal(
    await control.getByRole('progressbar').isVisible(),
    true,
    'Busy indicator stays visible.',
  );
  await control.evaluate((element) => element.click());
  await page.clock.runFor(50);
  assert.deepEqual(
    writes,
    [{ enabled: false }],
    'Pending clicks never issue a duplicate command.',
  );
  await page.screenshot({
    path: path.join(directory, `${page.viewportSize().width}-${phase}.png`),
    fullPage: true,
    animations: 'disabled',
  });
}

async function chooseDate(page, day) {
  await page
    .getByRole('main')
    .getByRole('button', { name: /choose date/i })
    .click();
  await page.clock.runFor(300);
  await page.getByRole('gridcell', { name: String(day), exact: true }).click();
  const accept = page.getByRole('button', { name: 'OK', exact: true });
  if (await accept.isVisible()) await accept.click();
  await page.clock.runFor(300);
}

async function inspect(browser, width) {
  const page = await browser.newPage({
    viewport: { width, height: 960 },
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
  });
  page.setDefaultTimeout(10_000);
  const errors = [];
  const violations = [];
  const writes = [];
  const briefDates = [];
  const commandSeen = deferred();
  const statusSeen = deferred();
  const current = structuredClone(fixture.data['/api/team']);
  current.latestRun = latest;
  let command;
  let holdStatus = false;
  const statuses = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.routeWebSocket('**/*', (socket) => {
    violations.push('Unexpected WebSocket: ' + socket.url());
    socket.close();
  });
  await page.route('**/*', async (route) => {
    try {
      const request = route.request();
      const address = new URL(request.url());
      assert.equal(
        address.origin,
        origin,
        'Every request stays on the synthetic origin.',
      );
      if (
        address.pathname === '/api/team/company' &&
        request.method() === 'POST'
      ) {
        const body = request.postDataJSON();
        assert.deepEqual(
          body,
          { enabled: false },
          'Only the synthetic Pause command is permitted.',
        );
        writes.push(body);
        assert.equal(
          writes.length,
          1,
          'The fixture must receive exactly one command.',
        );
        command = route;
        commandSeen.resolve();
        return;
      }
      assert.equal(request.method(), 'GET', 'Other writes are rejected.');
      if (address.pathname === '/api/team') {
        if (holdStatus) {
          statuses.push(route);
          statusSeen.resolve();
          return;
        }
        await route.fulfill({ json: structuredClone(current) });
      } else if (address.pathname === '/api/team/brief') {
        const date = address.searchParams.get('date');
        assert(
          [fixture.date, historicalDate].includes(date),
          'Only fixture report dates are available.',
        );
        briefDates.push(date);
        await route.fulfill({
          json: {
            date,
            runs: structuredClone(
              date === fixture.date ? [older, latest] : [historical],
            ),
          },
        });
      } else if (address.pathname.startsWith('/api/')) {
        assert(
          Object.hasOwn(fixture.data, address.pathname),
          'Unmocked API: ' + address.pathname,
        );
        await route.fulfill({
          json: structuredClone(fixture.data[address.pathname]),
        });
      } else {
        await builtAsset(route, address);
      }
    } catch (error) {
      violations.push(error.message);
      await route.abort('blockedbyclient');
    }
  });
  try {
    await page.clock.install({ time: new Date(fixture.at) });
    await page.clock.pauseAt(new Date(Date.parse(fixture.at) + 1000));
    await page.goto(`${origin}/daily-brief`);
    const main = page.getByRole('main');
    const pane = main.getByRole('region', {
      name: 'Research reports',
      exact: true,
    });
    const select = main.locator('#daily-brief-report-select');
    await pane.getByText(reviewerText, { exact: true }).waitFor();
    const collapse = page.getByRole('button', {
      name: 'Collapse team chat',
      exact: true,
    });
    if (await collapse.isVisible()) await collapse.click();
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.evaluate(() => document.fonts.ready);

    assert.equal(
      await select.locator('option').count(),
      2,
      'Both same-day reports are selectable.',
    );
    assert.equal(
      await select.inputValue(),
      latest.id,
      'Unsorted fixture still selects the newest report.',
    );
    assert.equal(
      await main.getByText(/^Next review /).count(),
      1,
      'Next review appears once.',
    );
    const researchDetails = main.getByText('Research details', { exact: true });
    const statusPanel = researchDetails.locator('xpath=../..');
    assert.equal(
      await statusPanel
        .getByRole('button', { name: 'Pause research', exact: true })
        .count(),
      1,
    );
    assert.equal(
      await statusPanel.getByText(/^Next review /).count(),
      1,
      'Research details share the status panel.',
    );

    const body = pane.getByText(reviewerText, { exact: true });
    const typography = await body.evaluate((element) => ({
      size: parseFloat(getComputedStyle(element).fontSize),
      lineHeight: parseFloat(getComputedStyle(element).lineHeight),
    }));
    assert.equal(typography.size, 16, 'Report text is 16px.');
    assert(
      Math.abs(typography.lineHeight - 26.4) < 0.2,
      'Report text uses a readable 1.65 line height.',
    );
    const details = pane.getByRole('group', {
      name: 'Report details',
      exact: true,
    });
    const layout = await pane.evaluate((element) => ({
      reportTop: element.getBoundingClientRect().top,
      documentWidth: document.documentElement.scrollWidth,
      viewportWidth: innerWidth,
    }));
    assert(
      layout.reportTop < (width < 600 ? 530 : 500),
      `Report begins near the first screen: ${layout.reportTop}px at ${width}px.`,
    );
    assert(
      layout.documentWidth <= width + 1,
      'No horizontal document overflow.',
    );
    const lastParagraph = pane.getByText('Latest observation 18:', {
      exact: false,
    });
    const lastBox = await lastParagraph.boundingBox();
    const detailsBox = await details.boundingBox();
    assert(
      lastBox && detailsBox && detailsBox.y >= lastBox.y + lastBox.height - 1,
      'Report details follow the main report text.',
    );
    await page.screenshot({
      path: path.join(directory, `${width}.png`),
      fullPage: true,
      animations: 'disabled',
    });

    const pause = main.getByRole('button', {
      name: 'Pause research',
      exact: true,
    });
    await pause.click();
    await received(commandSeen.promise, 'Pause command');
    await pendingControl(page, pause, writes, 'command-pending');
    holdStatus = true;
    current.company = {
      ...current.company,
      enabled: false,
      status: 'stopped',
      nextRunAt: null,
    };
    await command.fulfill({ json: structuredClone(current.company) });
    command = null;
    await received(statusSeen.promise, 'post-command status GET');
    await pendingControl(page, pause, writes, 'status-pending');
    holdStatus = false;
    for (const route of statuses.splice(0))
      await route.fulfill({ json: structuredClone(current) });
    const resume = main.getByRole('button', {
      name: 'Resume research',
      exact: true,
    });
    await resume.waitFor();
    assert.equal(
      await resume.isEnabled(),
      true,
      'Released status enables Resume.',
    );
    assert.deepEqual(writes, [{ enabled: false }]);

    await select.selectOption(older.id);
    await page.waitForURL((address) => address.hash === '#run-' + older.id);
    assert.equal(
      new URL(page.url()).searchParams.get('date'),
      fixture.date,
      'Report selection pins the displayed date even when the page URL had no date.',
    );
    const originalPane = await pane.elementHandle();
    await page.evaluate((id) => {
      window.location.hash = 'run-' + id;
    }, latest.id);
    await page.waitForFunction(
      (id) =>
        document.querySelector('#daily-brief-report-select')?.value === id,
      latest.id,
    );
    assert.equal(
      await pane.evaluate(
        (element, original) => element === original,
        originalPane,
      ),
      true,
      'Hash change updates the existing reader instance.',
    );

    await select.selectOption(older.id);
    await pane.locator('#run-' + older.id).waitFor();
    const scrollTop = await pane.evaluate((element) => {
      element.scrollTop = 180;
      return element.scrollTop;
    });
    assert(scrollTop > 0, 'Long synthetic report permits a reading position.');
    const readsBefore = briefDates.length;
    const polled = page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/team/brief',
    );
    await page.clock.runFor(10_100);
    await (await polled).finished();
    await page.clock.runFor(100);
    assert(
      briefDates.length > readsBefore,
      'The existing provider performs a real synthetic poll.',
    );
    assert.equal(
      await select.inputValue(),
      older.id,
      'Polling preserves the selected report.',
    );
    assert.equal(
      await pane.evaluate((element) => element.scrollTop),
      scrollTop,
      'Polling preserves the reading position.',
    );
    await originalPane.dispose();

    await chooseDate(page, 19);
    await page.waitForURL(
      (address) =>
        address.searchParams.get('date') === historicalDate &&
        address.hash === '',
    );
    await pane.getByText(historical.steps[0].output, { exact: true }).waitFor();
    assert.equal(
      await select.inputValue(),
      historical.id,
      'Date picker clears the previous report selection.',
    );
    await page.reload();
    await pane.getByText(historical.steps[0].output, { exact: true }).waitFor();
    assert.equal(
      await main.locator(`[data-date="${historicalDate}"]`).count(),
      1,
      'Reload retains the date picker value.',
    );
    assert.equal(new URL(page.url()).searchParams.get('date'), historicalDate);
    assert.equal(new URL(page.url()).hash, '');
    assert.equal(
      await select.inputValue(),
      historical.id,
      'Reload shows the selected historical date.',
    );
    assert(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth + 1,
      ),
      'No overflow after picker and reload.',
    );
    assert.deepEqual(violations, [], 'No external or unmocked requests.');
    assert.deepEqual(errors, [], 'No browser page errors.');
    return {
      width,
      ...layout,
      typography,
      companyPosts: writes.length,
      briefReads: briefDates.length,
      errors,
      violations,
    };
  } finally {
    if (command) await command.abort().catch(() => undefined);
    for (const route of statuses.splice(0))
      await route.abort().catch(() => undefined);
    await page.close();
  }
}

async function verify() {
  await fs.mkdir(directory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  try {
    for (const width of [320, 390, 820, 1440])
      results.push(await inspect(browser, width));
  } finally {
    await browser.close();
  }
  await fs.writeFile(
    path.join(directory, 'results.json'),
    JSON.stringify({ synthetic: true, origin, results }, null, 2),
  );
  console.log(
    'Daily Brief usability: four widths, readable report, compact status, pending command, hash/date continuity and polling passed; all requests fulfilled locally from built assets or synthetic data.',
  );
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
