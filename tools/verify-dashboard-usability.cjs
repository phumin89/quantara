const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');

const origin = 'http://127.0.0.1:3107';
const directory = path.resolve(
  process.env.DASHBOARD_CAPTURE_DIR ||
    'work/dashboard-usability-review-20261027',
);
const chartsOnly = process.argv.includes('--charts-only');
assert(
  process.argv.slice(2).every((argument) => argument === '--charts-only'),
  'Unknown driver option.',
);
const chartTitles = ['Live engine equity', 'Live realized net P/L'];
const widths = [1440, 1024, 768, 390, 320];
const dailyNet = [4, -3, 0, 7, -2, 3, 12.25];
const missingDate = '2026-10-17';
const trading = structuredClone(fixture.data['/api/trading']);
trading.reports = dailyNet
  .map((net, index) => ({
    ...trading.reports[0],
    date: `2026-10-${14 + index}`,
    createdAt: `2026-10-${14 + index}T02:59:00Z`,
    realizedNetThb: String(net),
    endingEquityThb: String(970 + index * 5),
  }))
  .filter((report) => report.date !== missingDate);

async function selected(page, date) {
  await page.waitForFunction(
    (date) =>
      document.querySelector('main [data-date]')?.getAttribute('data-date') ===
      date,
    date,
  );
  assert.match(
    await page.getByRole('main').innerText(),
    new RegExp('Confirmed engine exits: ' + date),
  );
}

function locationIs(page, date) {
  const address = new URL(page.url());
  assert.equal(
    address.searchParams.get('date'),
    date,
    'Date selection updates the query.',
  );
  assert.equal(
    address.searchParams.get('keep'),
    '1',
    'Unrelated query state survives date changes.',
  );
  assert.equal(
    address.hash,
    '#performance',
    'Date selection preserves the fragment.',
  );
}

async function collapseChat(page) {
  const collapse = page.getByRole('button', {
    name: 'Collapse team chat',
    exact: true,
  });
  if (
    (await collapse.isVisible()) &&
    (await collapse.getAttribute('aria-expanded')) === 'true'
  )
    await collapse.click();
  await page.clock.runFor(300);
  await page
    .getByRole('dialog', { name: 'Team chat', exact: true })
    .waitFor({ state: 'hidden' });
  await page.getByRole('main').waitFor();
}

async function choose(page, day, capture = false) {
  await collapseChat(page);
  await page
    .getByRole('main')
    .getByRole('button', { name: /choose date/i })
    .click();
  await page.clock.runFor(300);
  if (capture) {
    await page.screenshot({
      path: path.join(directory, 'phone-datepicker-open.png'),
      animations: 'disabled',
    });
  }
  await page.getByRole('gridcell', { name: String(day), exact: true }).click();
  const accept = page.getByRole('button', { name: 'OK', exact: true });
  if (await accept.isVisible()) await accept.click();
  await page.clock.runFor(300);
}

async function dates(page) {
  await selected(page, fixture.date);
  await choose(page, 19);
  await selected(page, '2026-10-19');
  locationIs(page, '2026-10-19');
  await page.reload();
  await collapseChat(page);
  await selected(page, '2026-10-19');
  locationIs(page, '2026-10-19');
  await choose(page, 18);
  await selected(page, '2026-10-18');
  locationIs(page, '2026-10-18');
  await page.goBack();
  await selected(page, '2026-10-19');
  locationIs(page, '2026-10-19');
  await page.goForward();
  await selected(page, '2026-10-18');
  locationIs(page, '2026-10-18');
  await page.evaluate(() => {
    const address = new URL(window.location.href);
    address.searchParams.set('date', '2026-10-17');
    history.pushState(history.state, '', address);
    window.dispatchEvent(new Event('quantara:route-change'));
  });
  await selected(page, '2026-10-17');
  locationIs(page, '2026-10-17');
  await page.goto(origin + '/dashboard?date=2026-02-30&keep=1#performance');
  await collapseChat(page);
  await selected(page, fixture.date);
}

async function themeColors(page) {
  return page.evaluate(() => {
    const probe = document.createElement('span');
    document.body.append(probe);
    try {
      return Object.fromEntries(
        Object.entries({
          ink: '--mui-palette-text-secondary',
          divider: '--mui-palette-divider',
          info: '--mui-palette-info-main',
          gain: '--mui-palette-success-main',
          loss: '--mui-palette-error-main',
          tableBackground: '--mui-palette-primary-main',
          tableInk: '--mui-palette-primary-contrastText',
        }).map(([name, token]) => {
          if (
            !getComputedStyle(document.documentElement)
              .getPropertyValue(token)
              .trim()
          )
            throw new Error('Missing semantic token: ' + token);
          probe.style.color = `var(${token})`;
          return [name, getComputedStyle(probe).color];
        }),
      );
    } finally {
      probe.remove();
    }
  });
}

async function measure(page) {
  return page.locator('main').evaluate((main) => {
    const box = (element) => {
      const rectangle = element.getBoundingClientRect();
      return {
        left: rectangle.left,
        right: rectangle.right,
        top: rectangle.top,
        bottom: rectangle.bottom,
        width: rectangle.width,
        height: rectangle.height,
      };
    };
    return {
      width: innerWidth,
      documentWidth: document.documentElement.scrollWidth,
      mainBounds: box(main),
      charts: [...main.querySelectorAll('svg[role="img"]')].map((svg) => ({
        title: svg.querySelector('title')?.textContent,
        bounds: box(svg),
        gridStrokes: [...svg.querySelectorAll('line')].map(
          (line) => getComputedStyle(line).stroke,
        ),
        seriesStrokes: [...svg.querySelectorAll('path')].map(
          (line) => getComputedStyle(line).stroke,
        ),
        seriesPaths: [...svg.querySelectorAll('path')].map((line) =>
          line.getAttribute('d'),
        ),
        pointFills: [...svg.querySelectorAll('circle')].map(
          (point) => getComputedStyle(point).fill,
        ),
        barFills: [...svg.querySelectorAll('rect')].map(
          (bar) => getComputedStyle(bar).fill,
        ),
        labels: [...svg.querySelectorAll('text')]
          .filter((text) => text.getClientRects().length > 0)
          .map((text) => {
            const matrix = text.getScreenCTM();
            const scale = matrix
              ? Math.min(
                  Math.hypot(matrix.a, matrix.b),
                  Math.hypot(matrix.c, matrix.d),
                )
              : 0;
            return {
              text: text.textContent,
              fill: getComputedStyle(text).fill,
              effectiveFontPx:
                parseFloat(getComputedStyle(text).fontSize) * scale,
              bounds: box(text),
            };
          }),
      })),
    };
  });
}

function chartGeometry(result, theme) {
  assert(
    result.documentWidth <= result.width + 1,
    `${result.width}px: no PAGE overflow.`,
  );
  assert.equal(
    result.charts.length,
    2,
    'Both populated retained-data charts are measured.',
  );
  for (const chart of result.charts) {
    assert(chartTitles.includes(chart.title));
    assert(chart.labels.length > 0, 'Chart labels are present.');
    for (const label of chart.labels) {
      assert.equal(
        label.fill,
        theme.ink,
        'Chart labels use the readable muted theme color.',
      );
      assert(
        label.effectiveFontPx >= 11.95,
        `${result.width}px ${chart.title} '${label.text}' is ${label.effectiveFontPx.toFixed(2)} CSS-px; minimum is 12.`,
      );
      assert(
        label.bounds.left >= chart.bounds.left - 1 &&
          label.bounds.right <= chart.bounds.right + 1 &&
          label.bounds.top >= chart.bounds.top - 1 &&
          label.bounds.bottom <= chart.bounds.bottom + 1,
        `${result.width}px: '${label.text}' fits inside the complete SVG, including horizontally scrollable content.`,
      );
    }
    assert.deepEqual(
      chart.gridStrokes,
      Array(3).fill(theme.divider),
      'Grid lines use the divider theme color.',
    );
    if (chart.title === 'Live engine equity') {
      assert.deepEqual(
        chart.seriesStrokes,
        [theme.info],
        'Equity line uses the information theme color.',
      );
      assert.deepEqual(
        chart.pointFills,
        Array(trading.reports.length).fill(theme.info),
        'Equity points retain the line color.',
      );
      assert.equal(
        chart.seriesPaths[0]?.match(/M/gi)?.length,
        2,
        'The missing retained day splits the line into two observed segments.',
      );
    } else {
      assert.deepEqual(
        chart.barFills,
        trading.reports.map((report) =>
          Number(report.realizedNetThb) < 0
            ? theme.loss
            : Number(report.realizedNetThb) > 0
              ? theme.gain
              : theme.ink,
        ),
        'Profit, loss and zero bars retain their semantic colors.',
      );
    }
    const dates = chart.labels
      .filter(
        (label) =>
          /^\d{2}-\d{2}$/.test(label.text) ||
          /^20\d{2}-\d{2}-\d{2}$/.test(label.text),
      )
      .sort((left, right) => left.bounds.left - right.bounds.left);
    assert(dates.length >= 2, 'Date ticks remain visible.');
    const missing = chart.labels.filter((label) => label.text === '—');
    assert.equal(
      missing.length,
      1,
      'The missing observation remains explicit.',
    );
    for (const date of dates) {
      for (const unknown of missing) {
        assert(
          date.bounds.right <= unknown.bounds.left ||
            date.bounds.left >= unknown.bounds.right ||
            date.bounds.bottom <= unknown.bounds.top ||
            date.bounds.top >= unknown.bounds.bottom,
          'Date labels do not overlap the missing-observation marker.',
        );
      }
    }
    for (let index = 1; index < dates.length; index++) {
      assert(
        dates[index - 1].bounds.right + 4 <= dates[index].bounds.left,
        `${result.width}px ${chart.title}: date labels retain a readable gap.`,
      );
    }
  }
}

async function keyboardCharts(page, theme) {
  const outcomes = [];
  for (const title of chartTitles) {
    const section = page
      .getByRole('main')
      .locator('section')
      .filter({ has: page.getByRole('heading', { name: title, exact: true }) });
    const scroller = section.getByRole('region');
    assert.equal(
      await scroller.count(),
      1,
      'Chart scroll area has one labelled region.',
    );
    assert(
      await scroller.getAttribute('aria-label'),
      'Chart scroll area has an accessible name.',
    );
    const summary = section.getByText('Chart data', { exact: true });
    await scroller.focus();
    const dimensions = await scroller.evaluate((element) => ({
      content: element.scrollWidth,
      width: element.clientWidth,
    }));
    let keyboardScroll = 0;
    let pointerScroll = 0;
    if (dimensions.content > dimensions.width + 1) {
      await scroller.evaluate((element) => {
        element.scrollLeft = 0;
      });
      for (let index = 0; index < 12; index++)
        await page.keyboard.press('ArrowRight');
      await page.clock.runFor(200);
      await page.waitForTimeout(250);
      keyboardScroll = await scroller.evaluate((element) => element.scrollLeft);
      assert(
        keyboardScroll > 0,
        'Keyboard can scroll narrow charts horizontally.',
      );
      await scroller.evaluate((element) => {
        element.scrollLeft = 0;
      });
      await scroller.hover();
      await page.mouse.wheel(10_000, 0);
      await page.clock.runFor(200);
      await page.waitForTimeout(250);
      pointerScroll = await scroller.evaluate((element) => element.scrollLeft);
      assert(
        pointerScroll > 0,
        'Pointer wheel can reach the end of a narrow chart.',
      );
      assert(
        await scroller.evaluate((element) => {
          const svg = element.querySelector('svg');
          const dates = [...svg.querySelectorAll('text')].filter(
            (text) =>
              /^\d{2}-\d{2}$/.test(text.textContent) ||
              /^20\d{2}-\d{2}-\d{2}$/.test(text.textContent),
          );
          const last = dates.at(-1)?.getBoundingClientRect();
          const bounds = element.getBoundingClientRect();
          return (
            last &&
            last.left >= bounds.left - 1 &&
            last.right <= bounds.right + 1
          );
        }),
        'The last date can be revealed inside the horizontal scroll region.',
      );
    }
    await scroller.focus();
    await page.keyboard.press('Tab');
    assert(
      await summary.evaluate((element) => document.activeElement === element),
      'Tab reaches Chart data from the chart without a trap.',
    );
    await page.keyboard.press('Enter');
    const table = section.getByRole('table');
    await table.waitFor({ state: 'visible' });
    const tableStyles = await table.evaluate((element) => ({
      borderCollapse: getComputedStyle(element).borderCollapse,
      headings: [...element.querySelectorAll('th')].map((cell) => ({
        background: getComputedStyle(cell).backgroundColor,
        color: getComputedStyle(cell).color,
        fontSize: parseFloat(getComputedStyle(cell).fontSize),
      })),
      amounts: [...element.querySelectorAll('tbody td:last-child')].map(
        (cell) => ({
          align: getComputedStyle(cell).textAlign,
          numeric: getComputedStyle(cell).fontVariantNumeric,
          borderColor: getComputedStyle(cell).borderBottomColor,
          borderStyle: getComputedStyle(cell).borderBottomStyle,
          fontSize: parseFloat(getComputedStyle(cell).fontSize),
        }),
      ),
    }));
    assert.equal(tableStyles.borderCollapse, 'collapse');
    for (const heading of tableStyles.headings) {
      assert.equal(
        heading.background,
        theme.tableBackground,
        'Data-table headings use the primary theme surface.',
      );
      assert.equal(
        heading.color,
        theme.tableInk,
        'Data-table headings use the primary contrast foreground.',
      );
      assert(
        heading.fontSize >= 14,
        'Data-table heading text remains readable.',
      );
    }
    for (const amount of tableStyles.amounts) {
      assert.equal(amount.align, 'right', 'Money columns align right.');
      assert(
        amount.numeric.includes('tabular-nums'),
        'Money columns use tabular figures.',
      );
      assert.equal(amount.borderColor, theme.divider);
      assert.equal(amount.borderStyle, 'solid');
      assert(amount.fontSize >= 14, 'Data-table values remain readable.');
    }
    assert.equal(
      await table.getByRole('row').count(),
      8,
      'All seven source observations remain in the accessible data table.',
    );
    assert.equal(
      await table
        .getByRole('cell', { name: fixture.date, exact: true })
        .count(),
      1,
      'The last source date remains available.',
    );
    assert.equal(
      await table
        .getByRole('row')
        .filter({ hasText: missingDate })
        .getByRole('cell')
        .last()
        .innerText(),
      'Unavailable',
      'The missing retained day remains unknown in the data table.',
    );
    if (page.viewportSize().width === 320 && title === chartTitles[0]) {
      await page.screenshot({
        path: path.join(directory, '320-chart-data.png'),
        fullPage: true,
        animations: 'disabled',
      });
    }
    await page.keyboard.press('Enter');
    outcomes.push({
      title,
      ...dimensions,
      keyboardScroll,
      pointerScroll,
      chartDataKeyboardReachable: true,
      tableStyles,
    });
  }
  return outcomes;
}

async function verify() {
  await fs.mkdir(directory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1000 },
    serviceWorkers: 'block',
    reducedMotion: 'reduce',
  });
  page.setDefaultTimeout(10_000);
  const errors = [],
    writes = [],
    violations = [],
    requests = [],
    results = [];
  const save = (status, failure = null) =>
    fs.writeFile(
      path.join(directory, chartsOnly ? 'charts-results.json' : 'results.json'),
      JSON.stringify(
        {
          synthetic: true,
          origin,
          chartsOnly,
          status,
          failure,
          errors,
          writes,
          violations,
          requests,
          results,
        },
        null,
        2,
      ),
    );
  page.on('pageerror', (error) => errors.push(error.message));
  await page.routeWebSocket('**/*', (socket) => {
    violations.push('Unexpected WebSocket: ' + socket.url());
    socket.close();
  });
  await page.route('**/*', async (route) => {
    const request = route.request();
    const address = new URL(request.url());
    if (request.method() !== 'GET')
      writes.push(request.method() + ' ' + address.pathname);
    try {
      assert.equal(address.origin, origin, 'Synthetic origin only.');
      assert.equal(
        request.method(),
        'GET',
        'Dashboard verification never writes.',
      );
      assert(
        !request.headers()['x-quantara-trading-control'],
        'No control authority in research fixtures.',
      );
      if (!address.pathname.startsWith('/api/'))
        return await staticBuildAsset(route, address);
      requests.push(address.pathname + address.search);
      assert(
        Object.hasOwn(fixture.data, address.pathname),
        'Unmocked API: ' + address.pathname,
      );
      const body = structuredClone(
        address.pathname === '/api/trading'
          ? trading
          : fixture.data[address.pathname],
      );
      if (address.pathname === '/api/team/brief') {
        assert.equal(address.searchParams.get('date'), fixture.date);
        body.date = fixture.date;
      }
      await route.fulfill({ json: body });
    } catch (error) {
      violations.push(error.message);
      await route.abort('blockedbyclient');
    }
  });
  try {
    await page.clock.install({ time: new Date(fixture.at) });
    await page.clock.pauseAt(new Date(Date.parse(fixture.at) + 1000));
    await page.goto(origin + '/dashboard?date=2026-10-20&keep=1#performance');
    const expand = page.getByRole('button', {
      name: 'Expand team chat',
      exact: true,
    });
    await expand.waitFor();
    assert.equal(
      await expand.getAttribute('aria-expanded'),
      'false',
      'Dashboard starts with team chat closed.',
    );
    await page
      .getByRole('main')
      .getByTestId('dashboard-day-net')
      .getByText('THB 12.25', { exact: true })
      .waitFor();
    await collapseChat(page);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: path.join(
        directory,
        chartsOnly ? 'charts-initial.png' : 'date-initial.png',
      ),
      fullPage: true,
      animations: 'disabled',
    });
    if (!chartsOnly) await dates(page);
    await page
      .getByRole('main')
      .getByTestId('dashboard-day-net')
      .getByText('THB 12.25', { exact: true })
      .waitFor();
    await collapseChat(page);
    const theme = await themeColors(page);
    for (const width of widths) {
      await page.setViewportSize({ width, height: 1000 });
      await page.clock.runFor(100);
      assert.equal(
        await expand.getAttribute('aria-expanded'),
        'false',
        'Changing viewport never opens team chat.',
      );
      await collapseChat(page);
      await page.evaluate(() => document.fonts.ready);
      await page.evaluate(() => window.scrollTo(0, 0));
      const measurement = await measure(page);
      results.push(measurement);
      await page.screenshot({
        path: path.join(directory, `${width}.png`),
        fullPage: true,
        animations: 'disabled',
      });
      await save('captured-before-assertions');
    }
    for (const width of widths) {
      await page.setViewportSize({ width, height: 1000 });
      await page.clock.runFor(100);
      await collapseChat(page);
      await page.evaluate(() => window.scrollTo(0, 0));
      const closed = await measure(page);
      await expand.click();
      await page
        .getByRole('dialog', { name: 'Team chat', exact: true })
        .waitFor();
      await page.clock.runFor(400);
      const opened = { ...(await measure(page)), chatExpanded: true };
      results.push(opened);
      await page.screenshot({
        path: path.join(directory, `${width}-chat.png`),
        fullPage: true,
        animations: 'disabled',
      });
      await collapseChat(page);
      const restored = await measure(page);
      opened.mainBoundsBeforeChat = closed.mainBounds;
      opened.mainBoundsAfterChat = restored.mainBounds;
      for (const [state, measurement] of [
        ['open', opened],
        ['closed', restored],
      ]) {
        for (const coordinate of [
          'left',
          'right',
          'top',
          'bottom',
          'width',
          'height',
        ]) {
          assert(
            Math.abs(
              measurement.mainBounds[coordinate] -
                closed.mainBounds[coordinate],
            ) <= 1,
            `${width}px: chat ${state} preserves main ${coordinate}.`,
          );
        }
      }
      await save('captured-before-assertions');
    }
    for (const result of results) chartGeometry(result, theme);
    for (const result of results.filter((result) => !result.chatExpanded)) {
      await page.setViewportSize({ width: result.width, height: 1000 });
      await page.clock.runFor(100);
      await collapseChat(page);
      result.keyboard = await keyboardCharts(page, theme);
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth + 1,
        ),
        'Keyboard/pointer access never creates PAGE overflow.',
      );
    }
    if (!chartsOnly) {
      await page.setViewportSize({ width: 390, height: 1000 });
      await choose(page, 19, true);
      await selected(page, '2026-10-19');
      locationIs(page, '2026-10-19');
      await page.screenshot({
        path: path.join(directory, 'phone-datepicker-selected.png'),
        animations: 'disabled',
      });
      assert(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth + 1,
        ),
        'Phone date selection creates no PAGE overflow.',
      );
    }
    assert.deepEqual(writes, []);
    assert.deepEqual(violations, []);
    assert.deepEqual(errors, []);
    await save('passed');
    console.log(
      chartsOnly
        ? 'Dashboard chart usability passed: synthetic GET-only built assets, responsive readable chart labels and keyboard/pointer data access; date behavior was skipped.'
        : 'Dashboard usability passed: synthetic GET-only built assets, date URL/reload/history/route events, responsive readable chart labels and keyboard/pointer data access.',
    );
  } catch (error) {
    await save('failed', error.message);
    await page
      .screenshot({
        path: path.join(
          directory,
          chartsOnly ? 'charts-failure.png' : 'failure.png',
        ),
        fullPage: true,
        animations: 'disabled',
      })
      .catch(() => undefined);
    throw error;
  } finally {
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
