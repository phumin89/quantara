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
  });
  await page.addInitScript(() => {
    window.addEventListener('message', (event) => {
      if (
        event.data?.source === 'quantara-home' &&
        event.data.type === 'snapshot'
      )
        window.officeSnapshot = event.data;
    });
    const clear = CanvasRenderingContext2D.prototype.clearRect;
    CanvasRenderingContext2D.prototype.clearRect = function (...args) {
      if (this.canvas.dataset.testid === 'office-canvas')
        window.officeFrames = (window.officeFrames || 0) + 1;
      return Reflect.apply(clear, this, args);
    };
  });
  const errors = [],
    writes = [],
    sockets = [];
  const diagnostics = [];
  page.on('console', (message) => {
    if (message.type() === 'error') diagnostics.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('websocket', (socket) => sockets.push(socket.url()));
  page.on('request', (request) => {
    if (request.url().includes('/api/') && request.method() !== 'GET')
      writes.push(request.method());
  });
  const at = new Date().toISOString();
  const run = {
    id: '218d734e-0446-492d-8b62-ffbd106d044e',
    accountKey: 'fixture-account-a',
    objective: 'Synthetic research',
    status: 'running',
    createdAt: at,
    updatedAt: at,
    error: null,
    steps: [
      {
        role: 'intraday',
        status: 'running',
        output: 'Fixture A: <script>window.unsafe=true</script>',
        error: null,
      },
      { role: 'reviewer', status: 'queued', output: null, error: null },
    ],
  };
  const company = {
    enabled: true,
    status: 'running',
    nextRunAt: null,
    lastRunId: run.id,
    error: null,
    activeRole: 'intraday',
  };
  const retained = {
    ...run,
    id: '118d734e-0446-492d-8b62-ffbd106d044e',
    status: 'cancelled',
    createdAt: new Date(Date.parse(at) - 120000).toISOString(),
    updatedAt: new Date(Date.parse(at) - 60000).toISOString(),
    steps: [
      {
        role: 'ceo',
        status: 'completed',
        output: 'Synthetic prior completed task.',
        error: null,
      },
      { role: 'analyst', status: 'cancelled', output: null, error: null },
      { role: 'reviewer', status: 'cancelled', output: null, error: null },
    ],
  };
  let overview = { configured: true, latestRun: run, company },
    runs = [retained, run],
    failure = false,
    reads = 0;
  await page.route('**/api/**', (route) =>
    route.fulfill({ status: 404, json: { error: 'Unmocked fixture API' } }),
  );
  await page.route('**/api/team', (route) => {
    reads++;
    return route.fulfill({
      status: failure ? 503 : 200,
      json: failure ? { error: 'Fixture offline' } : overview,
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
    await page.goto(url);
    const chatControl = page.getByRole('button', {
      name: 'Expand team chat',
      exact: true,
    });
    await chatControl.waitFor();
    assert.equal(await chatControl.getAttribute('aria-expanded'), 'false');
    await page.locator('iframe[data-ready="true"]').waitFor();
    const iframe = page.frameLocator(
      'iframe[title="Pixel Agents research office"]',
    );
    const canvas = iframe.getByTestId('office-canvas');
    await canvas.waitFor();
    await iframe.getByText('View only', { exact: true }).waitFor();
    assert.equal(await team.getByRole('button').count(), 8);
    assert(
      await canvas.evaluate((element) =>
        element
          .getContext('2d')
          .getImageData(0, 0, element.width, element.height)
          .data.some((v, i) => i % 4 !== 3 && v !== 0),
      ),
      'The original office draws real assets.',
    );
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .focus();
    await page.keyboard.press('Enter');
    assert.equal(await page.locator('#role-evidence').count(), 0);
    assert.equal(
      await team
        .getByRole('button', { name: 'Intraday Working', exact: true })
        .getAttribute('aria-pressed'),
      'true',
    );
    assert.equal(await page.evaluate(() => window.unsafe), undefined);
    const stable = async () => {
      await page.waitForTimeout(180);
      const before = await canvas.evaluate(() => window.officeFrames);
      await page.waitForTimeout(220);
      assert.equal(
        await canvas.evaluate(() => window.officeFrames),
        before,
        'Paused office must not keep rendering.',
      );
    };
    const moving = async (state) => {
      const before = await canvas.evaluate(() => window.officeFrames);
      assert(
        Number.isFinite(before),
        'The fixture must observe actual canvas rendering.',
      );
      await page.waitForTimeout(450);
      assert(
        await canvas.evaluate(
          (_element, count) => window.officeFrames > count + 2,
          before,
        ),
        `${state}: the upstream office loop must continue.`,
      );
    };
    await moving('Active');
    await page
      .getByRole('button', { name: 'Pause animation', exact: true })
      .click();
    await stable();
    await page
      .getByRole('button', { name: 'Resume animation', exact: true })
      .click();
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await stable();
    await page.emulateMedia({ reducedMotion: 'no-preference' });
    const waiting = {
      ...run,
      id: '418d734e-0446-492d-8b62-ffbd106d044e',
      status: 'completed',
      steps: run.steps.map((step) => ({
        ...step,
        status: 'completed',
        output: null,
      })),
    };
    overview = {
      configured: true,
      latestRun: waiting,
      company: { ...company, status: 'waiting', activeRole: null },
    };
    runs = [retained, waiting];
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Completed', exact: true })
      .waitFor();
    await moving('Waiting with no active role');
    assert(
      await canvas.evaluate(() =>
        window.officeSnapshot.agents.every(
          (agent) => agent.status !== 'active',
        ),
      ),
      'Ambient movement never invents an active role.',
    );
    overview = { ...overview, latestRun: null };
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Idle', exact: true })
      .waitFor();
    await moving('Idle');
    assert(
      await canvas.evaluate(() =>
        window.officeSnapshot.agents.every((agent) => agent.status === 'idle'),
      ),
    );
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await stable();
    const selectRoleAndCapture = async (label) => {
      const role = team.getByRole('button', {
        name: `${label} Idle`,
        exact: true,
      });
      await role.click();
      await role.waitFor({ state: 'visible' });
      assert.equal(await role.getAttribute('aria-pressed'), 'true');
      await page.waitForTimeout(200);
      return canvas.evaluate((element) => element.toDataURL());
    };
    const ceoRoom = await selectRoleAndCapture('CEO');
    const newsRoom = await selectRoleAndCapture('News');
    assert(
      ceoRoom !== newsRoom,
      'Selecting distinct parent roles must move the paused native camera.',
    );
    const officeContainer = canvas.locator('..');
    const originalWidth = await officeContainer.getAttribute('style');
    await officeContainer.evaluate((element) => {
      element.style.width = '90%';
    });
    await page.waitForTimeout(400);
    const hasRoom = () =>
      canvas.evaluate((element) => {
        const pixels = element
          .getContext('2d')
          .getImageData(0, 0, element.width, element.height).data;
        let furniture = 0;
        for (let index = 0; index < pixels.length; index += 4)
          if (
            pixels[index] > 60 &&
            pixels[index + 1] > 40 &&
            pixels[index] > pixels[index + 2] * 1.2
          )
            furniture++;
        return furniture > 100;
      });
    assert(await hasRoom(), 'A paused resized office must repaint its room.');
    await stable();
    await officeContainer.evaluate((element, style) => {
      element.setAttribute('style', style ?? '');
    }, originalWidth);
    await page.waitForTimeout(400);
    assert(await hasRoom(), 'The office must restore after a paused resize.');
    await page.emulateMedia({ reducedMotion: 'no-preference' });
    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', {
        configurable: true,
        value: true,
      });
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await stable();
    assert.equal(
      await canvas.evaluate(() => window.officeSnapshot.paused),
      true,
    );
    await page.evaluate(() => {
      delete document.hidden;
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await moving('Visible idle');
    await page.evaluate(() => {
      const spacer = document.createElement('div');
      spacer.id = 'fixture-offscreen-spacer';
      spacer.style.height = '2000px';
      document.body.append(spacer);
      window.scrollTo(0, document.body.scrollHeight);
    });
    await page.waitForTimeout(150);
    await stable();
    assert.equal(
      await canvas.evaluate(() => window.officeSnapshot.paused),
      true,
      'Offscreen office pauses.',
    );
    await page.evaluate(() => {
      document.getElementById('fixture-offscreen-spacer').remove();
      window.scrollTo(0, 0);
    });
    await moving('Onscreen idle');
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await stable();
    // One bounded visual round covers all supported sizes.
    await fs.mkdir('.impeccable/review', { recursive: true });
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      const chatToggle = await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .boundingBox();
      const refreshButton = await page
        .getByRole('button', { name: 'Refresh status', exact: true })
        .boundingBox();
      assert(chatToggle && refreshButton);
      assert(
        refreshButton.x + refreshButton.width <= chatToggle.x ||
          refreshButton.y >= chatToggle.y + chatToggle.height ||
          refreshButton.y + refreshButton.height <= chatToggle.y,
        `${name}: the collapsed chat toggle must not cover Refresh status.`,
      );
      assert(
        await canvas.evaluate(async (element) => {
          for (let attempt = 0; attempt < 30; attempt++) {
            await new Promise(requestAnimationFrame);
            const pixels = element
              .getContext('2d')
              .getImageData(0, 0, element.width, element.height).data;
            let furniture = 0;
            for (let index = 0; index < pixels.length; index += 4)
              if (
                pixels[index] > 60 &&
                pixels[index + 1] > 40 &&
                pixels[index] > pixels[index + 2] * 1.2
              )
                furniture++;
            if (furniture > 100) return true;
          }
          return false;
        }),
        `${name}: a paused resized office must still show the original room.`,
      );
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        `${name} fits.`,
      );
      assert(
        await team
          .getByRole('button')
          .evaluateAll((buttons) =>
            buttons.every(
              (button) => button.scrollWidth <= button.clientWidth + 1,
            ),
          ),
        `${name}: every role/status is readable within its control.`,
      );
      if (process.env.CAPTURE_HOME !== '0')
        await page.screenshot({
          path: `.impeccable/review/home-idle-${name}.png`,
          fullPage: true,
          animations: 'disabled',
        });
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.emulateMedia({ reducedMotion: 'no-preference' });
    overview = { configured: true, latestRun: run, company };
    runs = [retained, run];
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .waitFor();
    failure = true;
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Stale', exact: true })
      .waitFor();
    failure = false;
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Working', exact: true })
      .waitFor();
    const other = {
      ...run,
      id: '318d734e-0446-492d-8b62-ffbd106d044e',
      accountKey: 'fixture-account-b',
      status: 'completed',
      steps: [
        {
          role: 'intraday',
          status: 'completed',
          output: 'Fixture B evidence.',
          error: null,
        },
        { role: 'reviewer', status: 'completed', output: null, error: null },
      ],
    };
    overview = {
      configured: true,
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
      'Account switch clears old evidence.',
    );
    assert.equal(
      await page
        .getByRole('main')
        .getByText('Fixture B evidence.', { exact: true })
        .count(),
      0,
      'Home keeps full evidence in the archive.',
    );
    overview = { ...overview, latestRun: null };
    await refresh();
    await team
      .getByRole('button', { name: 'Intraday Idle', exact: true })
      .waitFor();
    overview = { ...overview, latestRun: other };
    runs = [other];
    await refresh();
    await page
      .getByRole('link', {
        name: 'Read research reports',
        exact: true,
      })
      .click();
    await page
      .getByRole('region', { name: 'Research reports', exact: true })
      .waitFor();
    await page.locator(`#run-${other.id}`).waitFor();
    const day = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Bangkok',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date(at));
    assert.equal(
      await page.locator('[data-date]').getAttribute('data-date'),
      day,
    );
    // The shared root provider continues one poll after route navigation.
    const before = reads;
    await page.waitForTimeout(10200);
    assert(reads - before <= 1, 'No duplicate polling after Home unmount.');
    await page.route('**/vendor/pixel-agents/office/index.html', (route) =>
      route.abort(),
    );
    await page.goto(url);
    await page
      .getByText('Office unavailable. Team status is available below.', {
        exact: true,
      })
      .waitFor();
    assert.deepEqual(writes, []);
    assert(
      sockets.every((socket) => socket.includes('/_next/')),
      'No upstream socket or terminal connection.',
    );
    assert.deepEqual(errors, []);
    console.log(
      'Home: original App/assets/layout, active-to-waiting/idle motion without invented work, explicit/hidden/offscreen/reduced-motion pause and resume, eight real states, GET-only, keyboard, responsive, failure/account isolation, deep link, shared poll and renderer fallback passed.',
    );
  } catch (error) {
    console.log({
      errors,
      diagnostics,
      frames: page.frames().map((frame) => frame.url()),
    });
    console.log(await page.locator('body').innerText());
    for (const frame of page.frames().slice(1))
      console.log(await frame.locator('body').innerText());
    throw error;
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
