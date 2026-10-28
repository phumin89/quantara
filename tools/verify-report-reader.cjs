const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function verify() {
  const url = process.env.TEST_URL;
  assert(
    url && /^http:\/\/127\.0\.0\.1:(3107|3108)$/.test(url),
    'Isolated fixture server only.',
  );
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
  });
  page.setDefaultTimeout(10000);
  let account = 'a',
    failHistory = false,
    holdHistorical = false,
    holdWallet = false;
  const held = [],
    errors = [],
    writes = [],
    remote = [],
    requests = [];
  const markdown = (role) =>
    '# Recorded summary\n\nSynthetic ' +
    role +
    ': evidence is incomplete; no verified profit.\n\n## Important points\n\n- Deposits are contributions, not income.\n- Losses and fees remain in the result.\n\n## Recorded status and reason\n\n| Item | Status | Reason |\n| --- | --- | --- |\n| Evidence | Limited | Missing complete account history |\n| Authority | Research only | No execution permission |\n\n## Detailed evidence and sources\n\n[Recorded source](https://example.com/research) and a note[^source].\n\n- [x] Retained observation\n- [ ] Missing evidence\n\n![tracking image](https://example.com/tracker.png)\n\n[unsafe script](javascript:alert(1)) [unsafe file](file:///private) [unsafe data](data:text/html,blocked) [unsafe network](//example.com/unsafe)\n\n<script>window.reportScriptRan=true</script>\n\n<iframe src="https://example.com/frame"></iframe>\n\n' +
    'Recorded limitation: synthetic observations do not establish a trading edge or verified company income. Preserve missing data and all costs.\n\n'.repeat(
      28,
    ) +
    '\nCompact table\n=============\n\nMetric | Value\n--- | ---\nRecorded observations | 12\n\n[^source]: Synthetic source note; no real account data.';
  const run = (day, key = account) => ({
    ...structuredClone(fixture.run),
    accountKey: fixture.run.accountKey + '-' + key,
    id:
      '00000000-0000-4000-8000-' +
      String((key === 'a' ? 100 : 200) + Number(day.slice(-2))).padStart(
        12,
        '0',
      ),
    createdAt: day + 'T02:58:00Z',
    updatedAt: day + 'T02:59:00Z',
    steps: fixture.run.steps.map((step) => ({
      ...step,
      output:
        day === fixture.date
          ? markdown(step.role)
          : 'Historical ' +
            key +
            ' ' +
            step.role +
            ' ' +
            day +
            '\nOriginal caveat: evidence remains limited.',
    })),
  });
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    const address = new URL(request.url());
    if (address.hostname === 'example.com') remote.push(address.pathname);
  });
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      address = new URL(request.url()),
      path = address.pathname,
      key = account;
    requests.push(path + address.search);
    if (request.method() !== 'GET') {
      writes.push(path);
      return route.abort();
    }
    let body = structuredClone(fixture.data[path]);
    assert(body, 'Every fixture endpoint is explicit: ' + path);
    if (path === '/api/team') body.latestRun = run(fixture.date, key);
    if (path === '/api/team/brief') {
      const day = address.searchParams.get('date');
      body = { date: day, runs: day === '2026-10-18' ? [] : [run(day, key)] };
      if (day === '2026-10-19' && holdHistorical) {
        held.push({ route, body });
        return;
      }
      if (day === '2026-10-19' && failHistory)
        return route.fulfill({
          status: 503,
          json: { error: 'Synthetic historical reports unavailable.' },
        });
    }
    if (path === '/api/crypto/portfolio') {
      body.positions[0].name =
        'Synthetic account ' + key.toUpperCase() + ' holding';
      if (holdWallet && key === 'a') {
        held.push({ route, body });
        return;
      }
    }
    if (path === '/api/trading' || path === '/api/crypto/risk') {
      body.accountKey = fixture.run.accountKey + '-' + key;
      if (body.capital) body.capital.accountKey = body.accountKey;
    }
    if (path === '/api/crypto/history' && holdWallet && key === 'a') {
      held.push({ route, body });
      return;
    }
    return route.fulfill({ json: body });
  });
  const release = async () => {
    for (const item of held.splice(0))
      await item.route.fulfill({ json: item.body }).catch(() => undefined);
  };
  const pane = page
    .getByRole('main')
    .getByRole('region', { name: 'Research reports', exact: true });
  const choose = async (day) => {
    await page
      .getByRole('main')
      .getByRole('button', { name: /choose date/i })
      .click();
    await page.clock.runFor(300);
    await page
      .getByRole('gridcell', { name: String(day), exact: true })
      .click();
    const ok = page.getByRole('button', { name: 'OK', exact: true });
    if (await ok.isVisible()) await ok.click();
    await page.clock.runFor(300);
  };
  try {
    await fs.mkdir('.impeccable/review', { recursive: true });
    await page.clock.install({ time: new Date(fixture.at) });
    await page.goto(url + '/daily-brief');
    await pane
      .getByRole('heading', { name: 'Recorded summary', exact: true })
      .first()
      .waitFor();
    assert.equal(await page.getByRole('main').locator('h1').count(), 1);
    await pane.getByText('Run status and evidence', { exact: true }).click();
    assert.equal(
      await pane
        .getByRole('table', { name: 'Recorded run status', exact: true })
        .count(),
      1,
    );
    assert.equal(
      await pane
        .getByRole('table', { name: 'Report table', exact: true })
        .count(),
      6,
    );
    assert.equal(
      await pane
        .getByRole('heading', { name: 'Compact table', exact: true })
        .count(),
      3,
    );
    assert.equal((await pane.getByRole('list').count()) > 0, true);
    assert.equal(await pane.locator('script,iframe,img').count(), 0);
    assert.equal(
      await pane
        .locator(
          'a[href^="javascript:"],a[href^="file:"],a[href^="data:"],a[href^="//"]',
        )
        .count(),
      0,
    );
    assert(
      await pane
        .getByRole('link', { name: 'Recorded source', exact: true })
        .first()
        .getAttribute('rel')
        .then((value) => value.includes('noopener')),
    );
    assert(
      await pane
        .locator('input[type="checkbox"]')
        .evaluateAll(
          (inputs) =>
            inputs.length === 6 && inputs.every((input) => input.disabled),
        ),
    );
    assert.equal(await page.evaluate(() => window.reportScriptRan), undefined);
    // Metadata is at the end; start the keyboard exercise at the report's beginning.
    await pane.evaluate((element) => {
      element.scrollTop = 0;
      element.focus({ preventScroll: true });
    });
    const pageY = await page.evaluate(() => window.scrollY);
    await page.keyboard.press('PageDown');
    await page.clock.runFor(200);
    await page.waitForTimeout(250);
    assert(
      await pane.evaluate((element) => element.scrollTop > 0),
      'Keyboard scrolls the report pane.',
    );
    assert.equal(
      await page.evaluate(() => window.scrollY),
      pageY,
      'Keyboard reading keeps the date/header in place.',
    );
    for (let index = 0; index < 80; index++) {
      await page.keyboard.press('Tab');
      if (
        await pane.evaluate(
          (element) => !element.contains(document.activeElement),
        )
      )
        break;
    }
    assert(
      await pane.evaluate(
        (element) => !element.contains(document.activeElement),
      ),
      'Tab can leave the report pane.',
    );
    if (
      await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .isVisible()
    )
      await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .click();
    await page
      .getByRole('complementary', { name: 'Team conversation', exact: true })
      .getByRole('heading', { name: 'Recorded summary', exact: true })
      .first()
      .waitFor();
    assert(
      await page.locator('[id]').evaluateAll((elements) => {
        const ids = elements
          .map((element) => element.id)
          .filter((id) => /fn-|fnref-|footnote-label/.test(id));
        return ids.length > 0 && new Set(ids).size === ids.length;
      }),
      'Archive and Chat footnote IDs are unique.',
    );
    assert(
      await page
        .locator('a[href^="#"]')
        .evaluateAll((links) =>
          links.every((link) =>
            document.getElementById(
              decodeURIComponent(link.getAttribute('href').slice(1)),
            ),
          ),
        ),
      'Footnote references and return links retain targets.',
    );
    await page
      .getByRole('button', { name: 'Collapse team chat', exact: true })
      .click();
    for (const [device, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 1024, 900],
      ['mobile', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      await pane.evaluate((element) => (element.scrollTop = 180));
      assert(
        await pane.evaluate(
          (element) =>
            element.clientHeight >= 320 &&
            element.clientHeight <= 720 &&
            element.scrollHeight > element.clientHeight,
        ),
      );
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth + 1,
        ),
      );
      if (device === 'mobile') {
        const table = pane
          .getByRole('region', {
            name: 'Report table horizontal scroll',
            exact: true,
          })
          .first();
        await table.focus();
        await page.keyboard.press('ArrowRight');
        await page.clock.runFor(200);
        await page.waitForTimeout(250);
        assert(
          await table.evaluate(
            (element) =>
              element.scrollLeft > 0 &&
              element.scrollHeight <= element.clientHeight + 2,
          ),
          'Table scrolls horizontally without a nested vertical scroller.',
        );
      }
      await page.screenshot({
        path:
          '.impeccable/review/release-reader-long-report-' + device + '.png',
        fullPage: true,
      });
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    holdHistorical = true;
    await choose(19);
    await choose(20);
    holdHistorical = false;
    await release();
    await pane
      .getByRole('heading', { name: 'Recorded summary', exact: true })
      .first()
      .waitFor();
    assert.equal(
      await pane.getByText(/Historical a reviewer/).count(),
      0,
      'Late historical response cannot overwrite current day.',
    );
    await choose(18);
    await pane.getByText('No activity', { exact: true }).waitFor();
    failHistory = true;
    await choose(19);
    await page
      .getByRole('alert')
      .filter({ hasText: 'Synthetic historical reports unavailable.' })
      .waitFor();
    failHistory = false;
    await page.getByRole('button', { name: 'Retry', exact: true }).click();
    await pane.getByText(/Historical a reviewer 2026-10-19/).waitFor();
    assert.equal(
      await pane
        .getByRole('heading', { name: 'Recorded summary', exact: true })
        .count(),
      0,
    );
    assert.equal(
      await pane.evaluate((element) => element.scrollTop),
      0,
      'A new selected day starts at its own beginning.',
    );
    await page.goto(url + '/crypto');
    await page
      .getByRole('button', { name: 'View BTC history', exact: true })
      .waitFor();
    await page.getByText('Crypto holdings value', { exact: true }).waitFor();
    await page
      .getByText('Unreserved broker balance', { exact: true })
      .waitFor();
    await page.getByText('Data details', { exact: true }).click();
    await page
      .getByRole('group', { name: 'Wallet data scope' })
      .getByText(/does not identify its account/)
      .waitFor();
    holdWallet = true;
    await page
      .getByRole('button', { name: 'View BTC history', exact: true })
      .click();
    await page.clock.runFor(15100);
    assert(
      held.length > 0,
      'Old account reads are held during the scope change.',
    );
    if (
      await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .isVisible()
    )
      await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .click();
    account = 'b';
    holdWallet = false;
    await page
      .getByRole('complementary', { name: 'Team conversation', exact: true })
      .getByRole('button', { name: 'Refresh team chat', exact: true })
      .click();
    await page.keyboard.press('Escape');
    await page.clock.runFor(250);
    await page.getByRole('main').waitFor();
    await page
      .getByRole('button', { name: 'View BTC history', exact: true })
      .filter({ hasText: 'Synthetic account B holding' })
      .waitFor();
    await release();
    await page.clock.runFor(100);
    assert.equal(
      await page
        .getByRole('button', { name: 'View BTC history', exact: true })
        .filter({ hasText: 'Synthetic account A holding' })
        .count(),
      0,
    );
    assert.equal(
      await page
        .getByRole('button', { name: 'View BTC history', exact: true })
        .getAttribute('aria-expanded'),
      'false',
      'Scope change clears old expanded history and the entire Crypto subtree.',
    );
    assert.deepEqual(writes, []);
    assert.deepEqual(remote, []);
    assert.deepEqual(errors, []);
    await fs.writeFile(
      '.impeccable/review/release-reader-regressions.json',
      JSON.stringify(
        {
          synthetic: true,
          commands: 0,
          remoteMarkdownRequests: remote,
          browserErrors: errors,
          checks: [
            'Markdown headings/lists/GFM tables',
            'HTML/images/unsafe schemes blocked',
            'disabled task checkboxes',
            'unique footnotes across archive and Chat',
            'keyboard and horizontal mobile scroll without traps',
            'selected-day bounds/reset/late race/empty/503 recovery',
            'Crypto source labels and account subtree isolation',
          ],
        },
        null,
        2,
      ),
    );
    console.log(
      'Report reader: safe Markdown/GFM, original caveats, unique footnotes, bounded day/keyboard/mobile tables, date race/empty/failure/retry and Crypto account isolation passed; GET-only fixtures.',
    );
  } catch (error) {
    console.error(
      await pane
        .evaluate((element) => ({
          height: element.clientHeight,
          content: element.scrollHeight,
          top: element.scrollTop,
          focused: document.activeElement === element,
          overflow: getComputedStyle(element).overflowY,
          pageY: window.scrollY,
        }))
        .catch(() => ({ pane: 'unmounted' })),
    );
    throw error;
  } finally {
    await release();
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
