const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const ts = require('typescript');
for (const extension of ['.ts', '.tsx'])
  require.extensions[extension] = (module, filename) => {
    module._compile(
      ts.transpileModule(require('node:fs').readFileSync(filename, 'utf8'), {
        compilerOptions: {
          module: ts.ModuleKind.CommonJS,
          target: ts.ScriptTarget.ES2022,
          esModuleInterop: true,
          jsx: ts.JsxEmit.ReactJSX,
        },
      }).outputText,
      filename,
    );
  };
const tradingSchema =
  require('../apps/web/features/trading/contracts/TradingOverview.ts').tradingOverviewSchema;
const riskSchema =
  require('../apps/web/features/crypto/contracts/RiskOverview.ts').riskOverviewSchema;
const briefSchema =
  require('../apps/web/features/team/contracts/DailyBrief.ts').dailyBriefSchema;

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
  const errors = [],
    writes = [],
    unmocked = [],
    requests = [],
    captures = [];
  let account = 'a',
    empty = false,
    failFinance = false,
    holdFinance = false;
  const held = [];
  const run = (date, key = account) => ({
    ...structuredClone(fixture.run),
    id:
      key === 'a' && date === fixture.date
        ? fixture.run.id
        : '00000000-0000-4000-8000-' +
          String((key === 'a' ? 100 : 200) + Number(date.slice(-2))).padStart(
            12,
            '0',
          ),
    accountKey: fixture.run.accountKey + '-' + key,
    objective:
      date === fixture.date
        ? fixture.run.objective
        : 'Historical report ' + date,
    createdAt: date + 'T02:58:00Z',
    updatedAt: date + 'T02:59:00Z',
    steps: fixture.run.steps.map((step) => ({
      ...step,
      output:
        date === fixture.date && key === 'a'
          ? step.output
          : 'Historical ' + key.toUpperCase() + ' ' + step.role + ' ' + date,
    })),
  });
  const finance = (path, key) => {
    const body = structuredClone(fixture.data[path]);
    body.accountKey = fixture.run.accountKey + '-' + key;
    if (path.endsWith('/risk')) {
      if (empty) {
        body.capital = null;
        body.performance = null;
      } else {
        body.capital.accountKey = body.accountKey;
        body.capital.equityThb = key === 'a' ? '15000' : '42000';
        body.performance = {
          from: '2026-10-19T03:00:00Z',
          to: fixture.at,
          netEquityChangeThb: '185.19',
          timeWeightedReturn: '0.0125',
          explanation: 'Synthetic complete cash-flow interval.',
        };
      }
      riskSchema.parse(body);
      return body;
    }
    body.mode = empty ? 'paper' : 'autoLive';
    body.rulesHash = 'a'.repeat(64);
    body.mandate = empty
      ? null
      : {
          id: 'synthetic-only',
          accountKey: body.accountKey,
          rulesHash: body.rulesHash,
          createdAt: fixture.at,
          expiresAt: '2026-10-27T03:00:00Z',
          revokedAt: null,
        };
    body.live = {
      ...body.live,
      equityThb: empty ? null : '1000',
      realizedNetThb: empty ? null : '12.25',
      realizedGrossThb: empty ? null : '13.50',
      feesThb: '1.25',
      unrealizedThb: empty ? null : '0',
    };
    body.paper = { ...body.paper, realizedNetThb: '99' };
    body.reports = empty
      ? []
      : [4, -3, 0, 7, -2, 3, 12.25].map((net, i) => ({
          ...body.reports[0],
          date: '2026-10-' + String(14 + i),
          realizedNetThb: String(net),
          endingEquityThb: String(970 + i * 5),
          createdAt: '2026-10-' + String(14 + i) + 'T02:59:00Z',
        }));
    body.autonomy = empty
      ? null
      : {
          observedAt: fixture.at,
          universeAt: fixture.at,
          totalMarkets: 10,
          eligibleMarkets: 8,
          checkedMarkets: 8,
          activity: 'Synthetic fixture only',
          nextCheckAt: null,
          capital: null,
          candidates: [],
          dailyProfitTargetThb: '50',
          realizedNetTodayThb: '12.25',
          evaluation: {
            protocol: 'ohlc-scenario-v1',
            verifiedProfit: false,
            observedAt: fixture.at,
            rulesHash: body.rulesHash,
            warnings: ['Synthetic small samples.'],
            metrics: ['breakout', 'pullback', 'range'].map((strategy, i) => ({
              symbol: 'BTC',
              timeframeSeconds: 300,
              segment: 'validation',
              strategy,
              samples: 12,
              trades: 2,
              unknownFills: 0,
              netThb: [8, -4, 0][i],
              feesThb: 1,
              maxDrawdownThb: 5,
              cashBenchmarkThb: 0,
              insufficientSamples: true,
            })),
          },
        };
    tradingSchema.parse(body);
    return body;
  };
  page.on('pageerror', (error) => errors.push(error.message));
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      address = new URL(request.url()),
      path = address.pathname,
      key = account;
    requests.push(path + address.search);
    if (request.method() !== 'GET') {
      writes.push(request.method() + ' ' + path);
      return route.abort();
    }
    let body = structuredClone(fixture.data[path]);
    if (!body) {
      unmocked.push(path);
      return route.fulfill({
        status: 404,
        json: { error: 'Unknown fixture endpoint' },
      });
    }
    if (path === '/api/team') body.latestRun = run(fixture.date, key);
    if (path === '/api/team/brief') {
      const date = address.searchParams.get('date');
      body = { date, runs: [run(date, key)] };
      briefSchema.parse(body);
    }
    if (path === '/api/trading' || path === '/api/crypto/risk') {
      body = finance(path, key);
      if (holdFinance) {
        held.push({ route, body });
        return;
      }
      if (failFinance)
        return route.fulfill({
          status: 503,
          json: { error: 'Synthetic finance unavailable' },
        });
    }
    return route.fulfill({ json: body });
  });
  const release = async () => {
    for (const item of held.splice(0))
      await item.route.fulfill({ json: item.body }).catch(() => undefined);
  };
  const settle = async () => {
    await page.waitForTimeout(75);
    await page
      .getByRole('progressbar', { name: 'Loading dashboard records' })
      .waitFor({ state: 'hidden' });
  };
  const choose = async (day) => {
    await page
      .getByRole('main')
      .getByRole('button', { name: /choose date/i })
      .click();
    await page.waitForTimeout(300);
    await page
      .getByRole('gridcell', { name: String(day), exact: true })
      .click();
    const ok = page.getByRole('button', { name: 'OK', exact: true });
    if (await ok.isVisible()) await ok.click();
    await page.waitForTimeout(300);
  };
  const navigate = async (path) => {
    await page.goto(url + path);
    await page.getByRole('main').getByRole('heading', { level: 1 }).waitFor();
    await settle();
    const chatControl = page.getByRole('button', {
      name: 'Expand team chat',
      exact: true,
    });
    await chatControl.waitFor();
    assert.equal(await chatControl.getAttribute('aria-expanded'), 'false');
  };
  try {
    await fs.mkdir('.impeccable/review', { recursive: true });
    // Keep evidence dates stable while letting canvas and ResizeObserver timing stay real.
    await page.clock.setFixedTime(new Date(fixture.at));
    for (const [device, width, height] of [
      ['desktop', 1440, 1000],
      ['tablet', 1024, 900],
      ['mobile', 390, 844],
    ]) {
      await page.setViewportSize({ width, height });
      for (const [name, path] of [
        ['home', '/'],
        ['dashboard', '/dashboard'],
        ['daily-brief', '/daily-brief'],
        ['crypto', '/crypto'],
      ]) {
        await navigate(path);
        const main = page.getByRole('main');
        const heading = main.getByRole('heading', { level: 1 });
        assert(
          await heading.evaluate(
            (element) =>
              Number.parseFloat(getComputedStyle(element).fontSize) >= 32,
          ),
          `${device}/${name}: page heading is smaller than 32px.`,
        );
        const actions = main.locator('.MuiButton-root:visible');
        for (const action of await actions.evaluateAll((elements) =>
          elements.map((element) => ({
            height: element.getBoundingClientRect().height,
            textTransform: getComputedStyle(element).textTransform,
          })),
        )) {
          assert(
            action.height >= 44,
            `${device}/${name}: action target is too small.`,
          );
          assert.equal(action.textTransform, 'none');
        }
        if (name === 'dashboard' && device === 'mobile') {
          const date = page.locator('[data-date]').first();
          const refresh = page.getByRole('button', {
            name: 'Refresh dashboard',
          });
          const [dateBox, refreshBox] = await Promise.all([
            date.boundingBox(),
            refresh.boundingBox(),
          ]);
          assert(
            dateBox && refreshBox && Math.abs(dateBox.y - refreshBox.y) < 8,
          );
          assert(refreshBox.x > dateBox.x);
        }
        await page
          .getByRole('button', {
            name: 'Expand team chat',
            exact: true,
          })
          .waitFor();
        assert.equal(
          await page
            .getByRole('button', { name: 'Team chat', exact: true })
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
            .getByRole('tab', { name: 'Team chat', exact: true })
            .count(),
          0,
        );
        if (name === 'home') {
          const canvas = page
            .frameLocator('iframe[title="Pixel Agents research office"]')
            .getByTestId('office-canvas');
          await canvas.waitFor();
          await canvas.evaluate(async () => {
            await document.fonts.ready;
            await new Promise(requestAnimationFrame);
            await new Promise(requestAnimationFrame);
          });
          let painted = false;
          for (let attempt = 0; attempt < 20 && !painted; attempt++) {
            await page.waitForTimeout(150);
            painted = await canvas.evaluate((element) => {
              const pixels = element
                .getContext('2d')
                .getImageData(0, 0, element.width, element.height).data;
              let furniture = 0;
              for (let i = 0; i < pixels.length; i += 4)
                if (
                  pixels[i] > 60 &&
                  pixels[i + 1] > 40 &&
                  pixels[i] > pixels[i + 2] * 1.2
                )
                  furniture++;
              return furniture > 100;
            });
          }
          assert(painted, 'Original office room has painted before capture.');
          await main
            .getByRole('region', { name: 'Team', exact: true })
            .waitFor();
          assert.equal(
            await main.getByRole('heading', { name: /evidence/i }).count(),
            0,
          );
          assert.equal(
            await main
              .getByText(
                'Synthetic Reviewer: evidence is limited; no execution authority.',
                { exact: true },
              )
              .count(),
            0,
          );
          if (device === 'desktop') {
            const office = await main
                .getByRole('region', { name: 'Research office', exact: true })
                .boundingBox(),
              team = await main
                .getByRole('region', { name: 'Team', exact: true })
                .boundingBox();
            assert(
              team.x >= office.x + office.width,
              'Team selectors sit beside the office.',
            );
          }
        }
        if (name === 'dashboard') {
          await page
            .getByTestId('dashboard-equity')
            .getByText('THB 15,000.00', { exact: true })
            .waitFor();
          assert.match(
            await page.getByTestId('dashboard-return').innerText(),
            /1.25%/,
          );
          assert.match(
            await page.getByTestId('dashboard-drawdown').innerText(),
            /Unavailable/,
          );
          assert.equal(
            await main
              .getByRole('img', { name: /\. THB\. Missing observations/ })
              .count(),
            3,
          );
          assert.equal(await main.getByText(/Synthetic Reviewer/).count(), 0);
          assert.equal(
            await main.getByText('Synthetic Bitcoin', { exact: true }).count(),
            0,
          );
        }
        if (name === 'daily-brief') {
          await main
            .getByRole('region', { name: 'Research reports' })
            .getByText(
              'Synthetic Reviewer: evidence is limited; no execution authority.',
              { exact: true },
            )
            .waitFor();
          assert.equal(
            await main
              .getByRole('heading', { name: 'Trading', exact: true })
              .count(),
            0,
          );
          assert.equal(
            await main
              .getByRole('region', { name: 'Daily conversation' })
              .count(),
            0,
          );
          assert(
            await main
              .getByRole('region', { name: 'Research reports' })
              .evaluate(
                (e) =>
                  e.clientHeight >= 320 &&
                  e.clientHeight <= 720 &&
                  getComputedStyle(e).overflowY === 'auto',
              ),
            'Selected-day reports use one bounded reader.',
          );
        }
        if (name === 'crypto') {
          await main
            .getByRole('button', { name: 'Disable', exact: true })
            .waitFor();
          await main
            .getByRole('button', { name: 'Emergency stop', exact: true })
            .waitFor();
          await main.getByRole('button', { name: /BTC/ }).first().waitFor();
          assert.equal(
            await main.getByRole('heading', { name: /risk sizing/i }).count(),
            0,
          );
          assert(
            await main
              .getByRole('button', { name: 'Disable', exact: true })
              .evaluate((e) => !e.closest('details:not([open])')),
          );
        }
        assert(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= window.innerWidth + 1,
          ),
          name + ' does not overflow ' + device,
        );
        const capture =
          '.impeccable/review/release-reader-' + name + '-' + device + '.png';
        // Full-page capture transiently resizes the paused iframe's canvas.
        // Inspect the office at its real device viewport; other pages capture fully.
        await page.screenshot({ path: capture, fullPage: name !== 'home' });
        captures.push(capture);
      }
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await navigate('/dashboard');
    const beforeDate = requests.length;
    await choose(19);
    await page.locator('[data-date="2026-10-19"]').waitFor();
    assert.match(
      await page.getByTestId('dashboard-day-net').innerText(),
      /THB\s3\.00/,
    );
    assert(
      !requests
        .slice(beforeDate)
        .some((path) => path.includes('/brief?date=2026-10-19')),
      'Dashboard dates do not request report content.',
    );
    for (let i = 0; i < 2; i++) {
      await choose(20);
      await choose(19);
    }
    empty = true;
    await page.getByRole('button', { name: 'Refresh dashboard' }).click();
    await settle();
    assert.match(
      await page.getByTestId('dashboard-equity').innerText(),
      /Unavailable/,
    );
    await page
      .getByText('No retained engine equity observations for this period.', {
        exact: true,
      })
      .waitFor();
    assert.equal(
      await page
        .getByRole('main')
        .getByRole('img', { name: /\. THB\. Missing observations/ })
        .count(),
      0,
    );
    empty = false;
    await page.getByRole('button', { name: 'Refresh dashboard' }).click();
    await settle();
    failFinance = true;
    await page.getByRole('button', { name: 'Refresh dashboard' }).click();
    await settle();
    await page
      .getByRole('alert')
      .filter({ hasText: 'Some data is unavailable.' })
      .waitFor();
    assert.match(
      await page.getByTestId('dashboard-equity').innerText(),
      /15,000/,
    );
    failFinance = false;
    await page.getByRole('button', { name: 'Refresh dashboard' }).click();
    await settle();
    holdFinance = true;
    await page.getByRole('button', { name: 'Refresh dashboard' }).click();
    await page
      .getByRole('progressbar', { name: 'Loading dashboard records' })
      .waitFor();
    await page
      .getByRole('button', { name: 'Expand team chat', exact: true })
      .click();
    account = 'b';
    holdFinance = false;
    await page
      .getByRole('complementary', { name: 'Team conversation', exact: true })
      .getByRole('button', { name: 'Refresh team chat' })
      .click();
    await page
      .getByTestId('dashboard-equity')
      .getByText('THB 42,000.00', { exact: true })
      .waitFor();
    await release();
    await settle();
    assert(
      !/15,000/.test(await page.getByTestId('dashboard-equity').innerText()),
      'Late old account response is ignored.',
    );
    await page.keyboard.press('Escape');
    await page
      .getByRole('complementary', { name: 'Team conversation', exact: true })
      .getByRole('region', { name: 'Daily conversation', exact: true })
      .waitFor({ state: 'hidden' });
    assert(
      await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .evaluate((e) => e === document.activeElement),
    );
    account = 'a';
    await navigate('/crypto');
    await page.locator('summary').filter({ hasText: 'Daily reports' }).click();
    await choose(19);
    await page
      .getByRole('heading', {
        name: 'Trading daily report · 2026-10-19',
        exact: true,
      })
      .waitFor();
    await choose(20);
    await page
      .getByRole('heading', {
        name: 'Trading daily report · 2026-10-20',
        exact: true,
      })
      .waitFor();
    await navigate('/daily-brief?date=2026-10-19');
    await page
      .getByRole('region', { name: 'Research reports' })
      .getByText('Historical A reviewer 2026-10-19', { exact: true })
      .waitFor();
    await choose(20);
    await page
      .getByRole('region', { name: 'Research reports' })
      .getByText(
        'Synthetic Reviewer: evidence is limited; no execution authority.',
        { exact: true },
      )
      .waitFor();
    await navigate('/daily-brief#run-' + fixture.run.id);
    await page.locator('#run-' + fixture.run.id).waitFor();
    assert.deepEqual(errors, []);
    assert.deepEqual(writes, []);
    assert.deepEqual(unmocked, []);
    await fs.writeFile(
      '.impeccable/review/release-reader-workspace.json',
      JSON.stringify(
        {
          synthetic: true,
          captures,
          browserErrors: errors,
          commands: writes,
          unmocked,
          checks: [
            'four canonical routes at desktop/tablet/mobile',
            'single chat launcher and minimize',
            'visible trading stop controls',
            'bounded selected-day reader and internal deep link',
            'KPI sources, true gaps, Live/Paper separation',
            'repeated date selection',
            'empty and failed refresh',
            'loading interruption and late old-account response',
          ],
        },
        null,
        2,
      ),
    );
    console.log(
      'Workspace release: 12 route captures; canonical ownership, visible stop controls, finance charts, date repetition, empty/failure/recovery, interrupted account race, archive/deep links; GET only, no browser errors.',
    );
  } catch (error) {
    await page.screenshot({
      path: '.impeccable/review/release-ia-failure.png',
      fullPage: true,
    });
    console.error((await page.getByRole('main').innerText()).slice(0, 5000));
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
