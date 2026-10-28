const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function verify() {
  const url = process.env.TEST_URL || 'http://127.0.0.1:3000';
  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  await fs.mkdir('.impeccable/review', { recursive: true });
  try {
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 820, 1180],
      ['phone', 390, 844],
    ]) {
      for (const scenario of ['race', 'historical', 'brief-error']) {
        const page = await browser.newPage({
          viewport: { width, height },
          serviceWorkers: 'block',
          reducedMotion: 'reduce',
        });
        page.setDefaultTimeout(10000);
        const current = structuredClone(fixture.data['/api/team']);
        const writes = [];
        const errors = [];
        let holdOld = false;
        let briefFailure = false;
        let oldRoute;
        let statusRoute;
        let receivedOld;
        let receivedStatus;
        const oldReceived = new Promise((resolve) => {
          receivedOld = resolve;
        });
        const statusReceived = new Promise((resolve) => {
          receivedStatus = resolve;
        });
        await page.route('**/api/**', async (route) => {
          const request = route.request();
          const target = new URL(request.url());
          if (request.method() !== 'GET') {
            writes.push({
              path: target.pathname,
              method: request.method(),
              body: request.postDataJSON(),
            });
            if (
              target.pathname !== '/api/team/company' ||
              request.method() !== 'POST'
            ) {
              await route.fulfill({
                status: 405,
                json: { error: 'Forbidden fixture write.' },
              });
              return;
            }
            current.company.enabled = false;
            current.company.status = 'stopped';
            current.company.nextRunAt = null;
            await route.fulfill({ json: current.company });
            return;
          }
          if (target.pathname === '/api/team') {
            if (holdOld) {
              holdOld = false;
              oldRoute = route;
              receivedOld();
            } else if (writes.length > 0 && !statusRoute) {
              statusRoute = route;
              receivedStatus();
            } else {
              await route.fulfill({ json: current });
            }
            return;
          }
          if (target.pathname === '/api/team/brief' && briefFailure) {
            await route.fulfill({
              status: 503,
              json: { error: 'Synthetic brief failure' },
            });
            return;
          }
          const body = structuredClone(fixture.data[target.pathname]);
          if (target.pathname === '/api/team/brief' && body)
            body.date = target.searchParams.get('date');
          await route.fulfill({
            status: body ? 200 : 404,
            json: body ?? { error: 'Fixture route unavailable.' },
          });
        });
        page.on('pageerror', (error) => errors.push(error.message));
        await page.clock.install({ time: new Date(fixture.at) });
        await page.clock.pauseAt(new Date(Date.parse(fixture.at) + 1000));
        await page.goto(
          `${url}/daily-brief${scenario === 'historical' ? '?date=2026-10-19' : ''}`,
        );
        const main = page.getByRole('main');
        const pause = main.getByRole('button', {
          name: 'Pause research',
          exact: true,
        });
        await pause.waitFor();
        const collapse = page.getByRole('button', {
          name: 'Collapse team chat',
          exact: true,
        });
        if (await collapse.isVisible()) await collapse.click();
        if (scenario === 'race') {
          holdOld = true;
          await page.clock.runFor(10000);
          await oldReceived;
        }
        briefFailure = scenario === 'brief-error';
        await pause.click();
        await statusReceived;
        assert.equal(
          await pause.isEnabled(),
          false,
          'Company control stays disabled through the status read.',
        );
        assert.deepEqual(writes, [
          {
            path: '/api/team/company',
            method: 'POST',
            body: { enabled: false },
          },
        ]);
        await statusRoute.fulfill({ json: current });
        const resume = main.getByRole('button', {
          name: 'Resume research',
          exact: true,
        });
        await resume.waitFor();
        if (oldRoute) {
          await oldRoute.fulfill({ json: fixture.data['/api/team'] });
          await page.clock.runFor(100);
          assert.equal(
            await resume.isVisible(),
            true,
            'A late pre-command response cannot restore the old company state.',
          );
        }
        if (briefFailure) {
          await main
            .getByRole('alert')
            .filter({ hasText: 'Synthetic brief failure' })
            .waitFor();
          await main
            .getByText('AI research stopped', { exact: true })
            .waitFor();
          await page.screenshot({
            path: `.impeccable/review/company-recovery-${name}.png`,
          });
        }
        assert.equal(await resume.isEnabled(), true);
        assert.equal(writes.length, 1, 'No command retry.');
        assert.deepEqual(errors, []);
        assert(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth + 1,
          ),
          'No page overflow.',
        );
        await page.close();
      }
    }
    console.log(
      'Company recovery browser checks passed: old-read race, historical date and independent brief failure at desktop/tablet/phone sizes; all API requests intercepted.',
    );
  } finally {
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
