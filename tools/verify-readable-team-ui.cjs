const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/readable-team-data.cjs');
async function verify() {
  assert.equal(process.env.TEST_URL, 'http://127.0.0.1:3108');
  const output = process.env.READABLE_CAPTURE_DIR;
  assert(output);
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const captures = [],
    errors = [],
    writes = [],
    escaped = [],
    results = [];
  try {
    for (const [name, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 768, 1024],
      ['mobile', 390, 844],
    ]) {
      const context = await browser.newContext({
        viewport: { width, height },
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
      });
      const page = await context.newPage();
      page.setDefaultTimeout(10000);
      let count = 3,
        account = 'a';
      await context.route('**/*', async (route) => {
        const request = route.request(),
          url = new URL(request.url());
        if (url.origin !== process.env.TEST_URL) {
          escaped.push(url.origin);
          await route.abort();
          return;
        }
        if (!url.pathname.startsWith('/api/')) {
          await route.continue();
          return;
        }
        if (request.method() !== 'GET') {
          writes.push(url.pathname);
          await route.fulfill({
            status: 405,
            json: { error: 'Fixture writes forbidden' },
          });
          return;
        }
        let body = structuredClone(fixture.base.data[url.pathname]);
        const runs = fixture.runs(count, account);
        if (url.pathname === '/api/team') body.latestRun = runs.at(-1);
        if (url.pathname === '/api/team/brief')
          body = { date: url.searchParams.get('date'), runs };
        if (
          url.pathname === '/api/trading' ||
          url.pathname === '/api/crypto/risk'
        ) {
          body.accountKey = 'synthetic-readable-' + account;
          if (body.capital) body.capital.accountKey = body.accountKey;
        }
        await route.fulfill({
          status: body ? 200 : 404,
          json: body ?? { error: 'Unmocked fixture route' },
        });
      });
      page.on('pageerror', (error) => errors.push(error.message));
      await page.clock.install({ time: new Date(fixture.at) });
      await page.goto(process.env.TEST_URL + '/daily-brief');
      const main = page.getByRole('main'),
        reports = main.getByRole('region', {
          name: 'Research reports',
          exact: true,
        });
      await reports
        .getByRole('heading', { name: 'Recorded summary', exact: true })
        .waitFor();
      await reports.getByText('Stopped · recorded', { exact: true }).waitFor();
      await reports
        .getByRole('alert')
        .getByText(
          'Synthetic evidence review failed; assessment remains unknown.',
          { exact: true },
        )
        .waitFor();
      await reports
        .getByRole('heading', { name: 'BTC/THB', exact: true })
        .waitFor();
      await reports
        .getByRole('heading', { name: 'TEST/THB', exact: true })
        .waitFor();
      assert.equal(await main.locator('h1').count(), 1);
      assert.equal(await reports.locator('script,iframe,img').count(), 0);
      assert.equal(
        await page.evaluate(() => window.syntheticReportScriptRan),
        undefined,
      );
      assert(
        (await reports
          .getByRole('heading', { name: 'Synthetic Markdown', exact: true })
          .count()) > 0,
      );
      await reports.getByText('Original report', { exact: true }).click();
      assert.equal(
        await reports
          .locator('pre')
          .filter({ hasText: 'adaptive-intraday-v2 | stopped' })
          .textContent(),
        fixture.plain,
      );
      await reports.getByText('Original report', { exact: true }).click();
      const metadata = reports.getByText('Source metadata', { exact: true });
      assert.equal(await metadata.locator('..').getAttribute('open'), null);
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
      );
      await reports.evaluate((element) => (element.scrollTop = 0));
      await page.evaluate(() => window.scrollTo(0, 0));
      const reportCapture = output + '/report-' + name + '.png';
      await page.screenshot({ path: reportCapture, fullPage: true });
      captures.push(reportCapture);
      const expand = page.getByRole('button', {
        name: 'Expand team chat',
        exact: true,
      });
      if (await expand.isVisible()) await expand.press('Enter');
      const dialog = page.getByRole('complementary', {
        name: 'Team conversation',
        exact: true,
      });
      await dialog
        .getByRole('region', { name: 'Daily conversation', exact: true })
        .waitFor();
      const collapse = page.getByRole('button', {
        name: 'Collapse team chat',
        exact: true,
      });
      assert.equal(await collapse.getAttribute('aria-expanded'), 'true');
      assert.equal(
        await page
          .getByRole('button', { name: 'Minimize team chat', exact: true })
          .count(),
        0,
      );
      assert.equal(
        await page
          .getByRole('button', { name: 'Close team chat', exact: true })
          .count(),
        0,
      );
      assert.equal(
        await page
          .getByRole('button', { name: 'Send research request', exact: true })
          .count(),
        0,
      );
      assert.equal(
        await page
          .getByRole('button', { name: 'Team chat', exact: true })
          .count(),
        0,
      );
      assert.equal(await dialog.getByRole('textbox').count(), 0);
      assert.equal(
        await dialog.locator('textarea,[contenteditable="true"]').count(),
        0,
      );
      assert.equal(
        await dialog
          .getByText(/Draft only|Research draft|Sending is not enabled/)
          .count(),
        0,
      );
      const conversation = dialog.getByRole('region', {
        name: 'Daily conversation',
        exact: true,
      });
      await conversation.evaluate((element) => (element.scrollTop = 160));
      await page.clock.runFor(50);
      const reading = await conversation.evaluate(
        (element) => element.scrollTop,
      );
      await collapse.click();
      await conversation.waitFor({ state: 'hidden' });
      const menu = page.getByRole('button', { name: 'Open menu', exact: true });
      if (await menu.isVisible()) {
        await menu.click();
        await page.clock.runFor(300);
      }
      await page.getByRole('link', { name: 'Dashboard', exact: true }).click();
      await main
        .getByRole('heading', { name: 'Dashboard', exact: true })
        .waitFor();
      await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .click();
      await dialog.waitFor();
      assert(
        Math.abs(
          (await conversation.evaluate((element) => element.scrollTop)) -
            reading,
        ) < 2,
        'Collapse/navigation preserves reading.',
      );
      count = 4;
      await dialog
        .getByRole('button', { name: 'Refresh team chat', exact: true })
        .click();
      await conversation
        .getByText('Synthetic new reply 4 from reviewer.', { exact: true })
        .waitFor();
      await dialog
        .getByRole('button', { name: 'New messages', exact: true })
        .waitFor();
      assert(
        Math.abs(
          (await conversation.evaluate((element) => element.scrollTop)) -
            reading,
        ) < 2,
        'New messages do not pull reading away.',
      );
      await dialog
        .getByRole('button', { name: 'New messages', exact: true })
        .click();
      count = 5;
      await dialog
        .getByRole('button', { name: 'Refresh team chat', exact: true })
        .click();
      await conversation
        .getByText('Synthetic new reply 5 from reviewer.', { exact: true })
        .waitFor();
      assert(
        await conversation.evaluate(
          (element) =>
            element.scrollHeight - element.clientHeight - element.scrollTop <
            50,
        ),
        'At-bottom reader follows new output.',
      );
      const box = await dialog.boundingBox();
      assert(box.height >= height * 0.6 && box.width <= width);
      const transcript = await conversation.boundingBox();
      assert(
        transcript.height >= box.height - 150 &&
          Math.abs(transcript.y + transcript.height - box.y - box.height) < 4,
        'The read-only conversation uses the panel through its bottom edge.',
      );
      const edge = await collapse.boundingBox();
      assert(
        edge.x >= 0 &&
          edge.x + edge.width <= width &&
          edge.y >= 0 &&
          edge.y + edge.height <= height,
      );
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
      );
      const chatCapture = output + '/chat-' + name + '.png';
      await page.screenshot({ path: chatCapture });
      captures.push(chatCapture);
      account = 'b';
      await dialog
        .getByRole('button', { name: 'Refresh team chat', exact: true })
        .click();
      await conversation
        .locator('#team-chat-run-00000000-0000-4000-8000-000000000105')
        .waitFor();
      assert.equal(
        await conversation
          .locator('#team-chat-run-00000000-0000-4000-8000-000000000001')
          .count(),
        0,
      );
      await collapse.focus();
      await page.keyboard.press('Escape');
      await conversation.waitFor({ state: 'hidden' });
      assert(
        await page
          .getByRole('button', { name: 'Expand team chat', exact: true })
          .evaluate((element) => element === document.activeElement),
      );
      results.push({
        name,
        report: true,
        chat: true,
        readOnlyViewing: true,
        scrollback: true,
        accountReset: true,
      });
      await context.close();
    }
    assert.deepEqual(writes, []);
    assert.deepEqual(escaped, []);
    assert.deepEqual(errors, []);
    await fs.writeFile(
      output + '/results.json',
      JSON.stringify(
        {
          results,
          captures,
          writes,
          escaped,
          errors,
          syntheticOnly: true,
          sendingEnabled: false,
        },
        null,
        2,
      ),
    );
    console.log(
      'Readable team: desktop/tablet/mobile report structure, errors/raw/fallback/Markdown safety; tall read-only conversation, no composer, one edge toggle, scrollback/follow/new messages and account reset passed. GET only.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
