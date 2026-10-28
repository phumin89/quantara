const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');

const origin = 'http://127.0.0.1:3108';
const directory = path.resolve(
  process.env.CRYPTO_HISTORY_CAPTURE_DIR || '.impeccable/review/crypto-history',
);
const trade = {
  transactionId: 'history-fixture',
  symbol: 'BTC',
  side: 'buy',
  quantity: 2,
  price: 321.45,
  fee: 0,
  feeCredit: 0,
  timestamp: fixture.at,
};

async function verify() {
  await fs.mkdir(directory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const results = [];
  try {
    for (const width of [1440, 768, 390]) {
      for (const scenario of [
        'timeout',
        'wrong-symbol',
        'invalid-time',
        'inherited-symbol',
        'duplicate-symbol',
      ]) {
        const symbol = scenario === 'inherited-symbol' ? 'constructor' : 'BTC';
        const expectedReads = ['inherited-symbol', 'duplicate-symbol'].includes(
          scenario,
        )
          ? 1
          : 2;
        const context = await browser.newContext({
          viewport: { width, height: 1000 },
          reducedMotion: 'reduce',
          serviceWorkers: 'block',
        });
        const page = await context.newPage();
        page.setDefaultTimeout(5000);
        const errors = [],
          violations = [],
          aborted = [];
        let historyReads = 0,
          portfolioReads = 0,
          invalidPortfolio = scenario === 'duplicate-symbol',
          held;
        page.on('pageerror', (error) => errors.push(error.message));
        page.on('requestfailed', (request) => aborted.push(request.url()));
        await page.routeWebSocket('**/*', (socket) => {
          violations.push('Unexpected WebSocket: ' + socket.url());
          socket.close();
        });
        await page.route('**/*', async (route) => {
          try {
            const request = route.request(),
              address = new URL(request.url());
            assert.equal(address.origin, origin);
            assert.equal(
              request.method(),
              'GET',
              'History checks never send commands.',
            );
            if (!address.pathname.startsWith('/api/'))
              return await staticBuildAsset(route, address);
            assert(
              Object.hasOwn(fixture.data, address.pathname),
              'Unmocked API: ' + address.pathname,
            );
            if (address.pathname === '/api/crypto/history') {
              assert.equal(address.searchParams.get('coin'), symbol);
              historyReads++;
              if (historyReads === 1 && scenario === 'timeout') {
                held = route;
                return;
              }
              let item = { ...trade, symbol };
              if (historyReads === 1 && scenario === 'wrong-symbol')
                item = { ...trade, symbol: 'ETH' };
              if (historyReads === 1 && scenario === 'invalid-time')
                item = { ...trade, timestamp: 'not-a-timestamp' };
              return await route.fulfill({
                json: { trades: [item], coverage: 'Synthetic fixture' },
              });
            }
            const payload = structuredClone(fixture.data[address.pathname]);
            if (address.pathname === '/api/crypto/portfolio') {
              portfolioReads++;
              if (invalidPortfolio) {
                payload.positions.push({
                  ...payload.positions[0],
                  symbol: 'btc',
                });
              }
            }
            if (
              address.pathname === '/api/crypto/portfolio' &&
              scenario === 'inherited-symbol'
            ) {
              payload.positions[0].symbol = symbol;
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
          if (scenario === 'duplicate-symbol') {
            const error = main
              .getByRole('alert')
              .filter({ hasText: 'Portfolio unavailable' });
            await error
              .getByText('Portfolio response is invalid.', { exact: true })
              .waitFor();
            assert.equal(await main.getByTestId('total-assets').count(), 0);
            invalidPortfolio = false;
            await error
              .getByRole('button', { name: 'Retry', exact: true })
              .click();
          }
          const toggle = main.getByRole('button', {
            name: `View ${symbol} history`,
            exact: true,
          });
          await toggle.waitFor();
          const collapse = page.getByRole('button', {
            name: 'Collapse team chat',
            exact: true,
          });
          if (await collapse.isVisible()) await collapse.click();
          await toggle.click();
          const region = main.getByRole('region', {
            name: `${symbol} trade history`,
            exact: true,
          });
          if (!['inherited-symbol', 'duplicate-symbol'].includes(scenario)) {
            if (scenario === 'timeout') {
              await region
                .getByText('Loading history…', { exact: true })
                .waitFor();
              assert(held);
              await page.clock.fastForward(8001);
              await region
                .getByText('History request timed out. Try again.', {
                  exact: true,
                })
                .waitFor();
              assert(
                aborted.some((url) => url.includes('/api/crypto/history?')),
              );
            } else {
              await region
                .getByText('History response is invalid.', { exact: true })
                .waitFor();
            }
            assert.equal(
              await region
                .getByText('Loading history…', { exact: true })
                .count(),
              0,
            );
            await page.clock.runFor(300);
            await region.screenshot({
              path: path.join(directory, `${width}-${scenario}-error.png`),
              animations: 'disabled',
            });
            const retryBounds = await region
              .getByRole('button', { name: 'Retry', exact: true })
              .boundingBox();
            assert(
              retryBounds &&
                retryBounds.x >= 0 &&
                retryBounds.x + retryBounds.width <= width,
              'Retry must be visible without horizontally scrolling the history table.',
            );
            await region
              .getByRole('button', { name: 'Retry', exact: true })
              .click();
          }
          await region
            .getByRole('cell')
            .filter({ hasText: /321\.45/ })
            .waitFor();
          assert.equal(
            historyReads,
            expectedReads,
            'Only the expected explicit history reads start.',
          );
          if (scenario === 'duplicate-symbol') assert(portfolioReads >= 2);
          if (held) {
            await held
              .fulfill({
                json: {
                  trades: [{ ...trade, price: 987.65 }],
                  coverage: 'Late old response',
                },
              })
              .catch(() => {});
            await page.clock.runFor(100);
            assert.equal(
              await region
                .getByRole('cell')
                .filter({ hasText: /987\.65/ })
                .count(),
              0,
            );
          }
          await page.clock.runFor(300);
          await toggle.click();
          await page.clock.runFor(300);
          await toggle.click();
          await page.clock.runFor(300);
          await region
            .getByRole('cell')
            .filter({ hasText: /321\.45/ })
            .waitFor();
          assert.equal(
            historyReads,
            expectedReads,
            'Reopening verified history uses the existing cache.',
          );
          assert.equal(await region.getByRole('row').count(), 2);
          assert.equal(
            await page.evaluate(
              () => document.documentElement.scrollWidth <= innerWidth + 1,
            ),
            true,
          );
          await region.screenshot({
            path: path.join(directory, `${width}-${scenario}.png`),
            animations: 'disabled',
          });
          assert.deepEqual(errors, []);
          assert.deepEqual(violations, []);
          results.push({
            width,
            scenario,
            historyReads,
            portfolioReads,
            errors,
            violations,
            writes: 0,
          });
        } catch (error) {
          await page.screenshot({
            path: path.join(directory, `${width}-${scenario}-failure.png`),
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
      `Crypto history: ${results.length} desktop/tablet/phone timeout, invalid-data, inherited-name and cache cases passed; synthetic GETs and built assets only.`,
    );
  } finally {
    await browser.close();
  }
}

verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
