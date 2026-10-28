const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');
const directory = path.resolve(
  process.env.TRADING_MONEY_CAPTURE_DIR || '.impeccable/review/trading-money',
);
const origin = 'http://127.0.0.1:3108';
const position = {
  symbol: 'BTC',
  quantity: '1',
  costThb: '0.004',
  openedAt: fixture.at,
  peakBid: '1',
};

async function verify() {
  await fs.mkdir(directory, { recursive: true });
  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHANNEL,
    headless: true,
  });
  const results = [];
  try {
    for (const width of [1440, 768, 390]) {
      for (const { mode, adaptive } of [
        { mode: 'paper', adaptive: false },
        { mode: 'stopped', adaptive: false },
        { mode: 'paper', adaptive: true },
        { mode: 'stopped', adaptive: true },
        { mode: 'manualReview', adaptive: true },
      ]) {
        const caseName = `${width}-${adaptive ? 'adaptive-' : ''}${mode}`;
        const context = await browser.newContext({
          viewport: { width, height: 1000 },
          serviceWorkers: 'block',
          reducedMotion: 'reduce',
        });
        const page = await context.newPage();
        page.setDefaultTimeout(10000);
        const errors = [],
          violations = [];
        page.on('pageerror', (error) => errors.push(error.message));
        await page.routeWebSocket('**/*', (socket) => {
          violations.push('Unexpected WebSocket');
          socket.close();
        });
        await page.route('**/*', async (route) => {
          try {
            const request = route.request(),
              address = new URL(request.url());
            assert.equal(address.origin, origin);
            assert.equal(request.method(), 'GET');
            if (!address.pathname.startsWith('/api/'))
              return await staticBuildAsset(route, address);
            assert(
              Object.hasOwn(fixture.data, address.pathname),
              'Unmocked API: ' + address.pathname,
            );
            const payload = structuredClone(fixture.data[address.pathname]);
            if (address.pathname === '/api/trading') {
              payload.mode = mode;
              if (adaptive) {
                payload.rules.version = 'adaptive-intraday-v2';
                payload.rules.symbols = [];
                payload.rules.adaptive = {
                  universeVersion: 'bitkub-thb-spot-v1',
                  includeFutureDeposits: true,
                  excludedSymbols: [],
                  risk: {
                    ...fixture.data['/api/crypto/risk'].policy,
                    profile: 'aggressive',
                    maximumSpreadFraction: payload.rules.maxSpreadBps / 10000,
                    estimatedFeeRate: payload.rules.feeBps / 10000,
                  },
                  dailyLossFraction: 0.1,
                  dailyProfitTargetThb: 100,
                };
                payload.autonomy = {
                  observedAt: fixture.at,
                  universeAt: fixture.at,
                  totalMarkets: 1,
                  eligibleMarkets: 1,
                  checkedMarkets: 1,
                  activity:
                    'Synthetic autonomous status; no trading authority.',
                  nextCheckAt: null,
                  capital: {
                    observedAt: fixture.at,
                    availableThb: '9007199254740993',
                    equityThb:
                      mode === 'paper'
                        ? '12345.67'
                        : '79228162514264337593543950335',
                    exposureThb: '0',
                    reservedThb: '9007199254740993.005',
                    blocker: null,
                  },
                  candidates: [],
                  dailyProfitTargetThb: '100',
                  realizedNetTodayThb: null,
                };
                if (mode !== 'manualReview') {
                  payload.objective = {
                    id: 'synthetic-objective',
                    targetThb: 10000,
                    deadlineExclusive: '2026-11-01T00:00:00+07:00',
                    includesDeposits: true,
                    timeZone: 'Asia/Bangkok',
                    observedAt: mode === 'paper' ? fixture.at : null,
                    portfolioValueThb:
                      mode === 'paper' ? '79228162514264337593543950335' : null,
                    remainingThb: mode === 'paper' ? '9007199254740993' : null,
                    depositsThb:
                      mode === 'paper' ? '9007199254740993.005' : null,
                    tradingPnlThb:
                      mode === 'paper' ? '-9007199254740993.005' : null,
                    valuationStatus: mode === 'paper' ? 'verified' : 'unknown',
                    reason:
                      mode === 'paper'
                        ? null
                        : 'Synthetic valuation unavailable.',
                  };
                }
              }
              const book = mode === 'paper' ? payload.paper : payload.live;
              book.cashThb =
                mode === 'paper'
                  ? '9007199254740993'
                  : '-1.0049999999999999999999999999';
              book.equityThb =
                mode === 'paper' ? '79228162514264337593543950335' : null;
              book.realizedNetThb =
                mode === 'paper' ? '1.0049999999999999999999999999' : null;
              book.positions =
                mode === 'paper'
                  ? [position, { ...position, symbol: 'ETH' }]
                  : [{ ...position, costThb: '9007199254740993.005' }];
            }
            await route.fulfill({ json: payload });
          } catch (error) {
            violations.push(error.message);
            await route.abort('blockedbyclient');
          }
        });
        try {
          await page.clock.install({ time: new Date(fixture.at) });
          await page.goto(origin + '/crypto');
          const main = page.getByRole('main');
          await main
            .getByText('Crypto holdings value', { exact: true })
            .waitFor();
          const collapse = page.getByRole('button', {
            name: 'Collapse team chat',
            exact: true,
          });
          if (await collapse.isVisible()) await collapse.click();
          await page
            .locator('#trading summary')
            .filter({ hasText: /^Orders & positions$/ })
            .click();
          const ledger = page.locator('dl').filter({
            has: page.locator(
              `dt:text-is("${mode === 'paper' ? 'Simulated cash' : adaptive ? 'Observed available THB' : 'Ledger cash'}")`,
            ),
          });
          await ledger.waitFor();
          const expected =
            mode === 'paper'
              ? [
                  ['Simulated cash', '฿9,007,199,254,740,993'],
                  ['Simulated cost', '฿0.01'],
                  [
                    adaptive
                      ? 'Simulated net today'
                      : 'Simulated realized net P&L',
                    adaptive ? 'Unknown' : '฿1',
                  ],
                ]
              : [
                  [
                    adaptive ? 'Observed available THB' : 'Ledger cash',
                    adaptive ? '฿9,007,199,254,740,993' : '฿-1',
                  ],
                  [
                    adaptive ? 'Observed exposure incl. orders' : 'Ledger cost',
                    adaptive ? '฿0' : '฿9,007,199,254,740,993.01',
                  ],
                  [adaptive ? 'Net today' : 'Realized net P&L', 'Unknown'],
                ];
          for (const [label, amount] of expected) {
            const value = ledger
              .locator('dt')
              .filter({ hasText: label })
              .locator('..')
              .locator('dd');
            assert.equal(await value.innerText(), amount, label);
          }
          const details = page.locator('#trading summary').filter({
            hasText:
              mode === 'paper'
                ? 'Ledger details · 2 lots'
                : 'Ledger details · 1 lot',
          });
          await details.click();
          const book = details.locator('..');
          await book
            .getByText(
              mode === 'paper' || adaptive
                ? /Equity ฿79,228,162,514,264,337,593,543,950,335/
                : /Equity Unknown/,
            )
            .waitFor();
          let objectiveExpected = null;
          let objectiveLayout = null;
          let capitalExpected = null;
          let capitalFits = null;
          await page.evaluate(() => document.fonts.ready);
          await page.clock.runFor(300);
          if (adaptive) {
            const system = page.locator('#trading summary').filter({
              hasText: /^System details$/,
            });
            await system.click();
            const metrics = page.locator(
              'dl[aria-label="Portfolio objective"]',
            );
            await metrics.waitFor();
            objectiveExpected = [
              ['Portfolio value', '฿79,228,162,514,264,337,593,543,950,335'],
              ['Remaining', '฿9,007,199,254,740,993'],
              ['Deposits', '฿9,007,199,254,740,993.01'],
              ['Trading P&L', '฿-9,007,199,254,740,993.01'],
            ].map(([label, amount]) => [
              label,
              mode === 'paper' ? amount : 'Unknown',
            ]);
            for (const [label, amount] of objectiveExpected) {
              const value = metrics
                .locator('dt')
                .filter({ hasText: label })
                .locator('..')
                .locator('dd');
              assert.equal((await value.textContent()).trim(), amount, label);
            }
            if (mode === 'manualReview') {
              await system
                .locator('..')
                .getByText(
                  'This snapshot does not include an account target.',
                  {
                    exact: true,
                  },
                )
                .waitFor();
            }
            const capital = system
              .locator('..')
              .getByText(
                mode === 'paper' ? /^Simulated equity ฿/ : /^Equity ฿/,
              );
            capitalExpected =
              mode === 'paper'
                ? 'Simulated equity ฿79,228,162,514,264,337,593,543,950,335'
                : 'Equity ฿79,228,162,514,264,337,593,543,950,335 · Reserved ฿9,007,199,254,740,993.01';
            assert.equal((await capital.textContent()).trim(), capitalExpected);
            objectiveLayout = await metrics
              .locator('dd')
              .evaluateAll((elements) =>
                elements.map((element) => {
                  const bounds = element.getBoundingClientRect();
                  const range = document.createRange();
                  range.selectNodeContents(element);
                  return {
                    text: element.textContent,
                    width: element.clientWidth,
                    scrollWidth: element.scrollWidth,
                    outside: Array.from(range.getClientRects()).some(
                      (rect) =>
                        rect.left < bounds.left - 1 ||
                        rect.right > bounds.right + 1,
                    ),
                  };
                }),
              );
            for (const value of objectiveLayout) {
              assert(
                value.width > 0 &&
                  value.scrollWidth <= value.width + 1 &&
                  !value.outside,
                `${width}/${mode} objective amount overflows: ${JSON.stringify(value)}`,
              );
            }
            capitalFits = await capital.evaluate((element) => {
              const bounds = element.getBoundingClientRect();
              const range = document.createRange();
              range.selectNodeContents(element);
              return (
                element.scrollWidth <= element.clientWidth + 1 &&
                Array.from(range.getClientRects()).every(
                  (rect) =>
                    rect.left >= bounds.left - 1 &&
                    rect.right <= bounds.right + 1,
                )
              );
            });
            assert(
              capitalFits,
              `${width}/${mode} capital text must fit inside System details`,
            );
            await metrics.screenshot({
              path: path.join(directory, `${caseName}-objective.png`),
              animations: 'disabled',
            });
          }
          assert.equal(
            await page.evaluate(
              () => document.documentElement.scrollWidth <= innerWidth + 1,
            ),
            true,
          );
          await ledger.screenshot({
            path: path.join(directory, `${caseName}.png`),
            animations: 'disabled',
          });
          assert.deepEqual(errors, []);
          assert.deepEqual(violations, []);
          results.push({
            width,
            mode,
            adaptive,
            expected,
            objectiveExpected,
            objectiveLayout,
            capitalExpected,
            capitalFits,
            errors,
            violations,
            writes: 0,
          });
        } catch (error) {
          await page.screenshot({
            path: path.join(directory, `${caseName}-failure.png`),
            fullPage: true,
            animations: 'disabled',
          });
          throw error;
        } finally {
          await context.close();
        }
      }
    }
    await fs.writeFile(
      path.join(directory, 'results.json'),
      JSON.stringify(
        { synthetic: true, serverStarted: false, results },
        null,
        2,
      ),
    );
    console.log(
      'Trading money: fifteen desktop/tablet/phone legacy/adaptive exact-string, aggregate-rounding, objective layout and Unknown/missing cases passed; intercepted GETs only.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
