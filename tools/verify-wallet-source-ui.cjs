const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');

async function verify() {
  assert.equal(process.env.TEST_URL, 'http://127.0.0.1:3108');
  const output = process.env.WALLET_CAPTURE_DIR;
  assert(output, 'Use a private fixture capture directory.');
  const browser = await chromium.launch({
    headless: true,
    channel: process.env.PLAYWRIGHT_CHANNEL,
  });
  const writes = [],
    escaped = [],
    errors = [],
    captures = [],
    results = [];
  await fs.mkdir(output, { recursive: true });
  try {
    for (const [device, width, height] of [
      ['desktop', 1440, 1000],
      ['mobile', 390, 844],
    ]) {
      for (const scenario of [
        'zero',
        'stale',
        'missing',
        'missing-zero',
        'future-cached',
        'error',
        'cached-error',
        'reserved',
      ]) {
        const context = await browser.newContext({
          viewport: { width, height },
          reducedMotion: 'reduce',
          serviceWorkers: 'block',
        });
        const page = await context.newPage();
        page.setDefaultTimeout(10000);
        const wallet = structuredClone(fixture.data['/api/crypto/portfolio']);
        const trading = structuredClone(fixture.data['/api/trading']);
        trading.mode = 'stopped';
        trading.live.positions = [
          {
            symbol: 'BTC',
            quantity: '0.2',
            costThb: '100',
            openedAt: fixture.at,
            peakBid: '600',
          },
        ];
        wallet.performance = {
          ...wallet.performance,
          realizedPnl: null,
          unrealizedPnl: null,
          totalPnl: null,
          unreconciledSymbols: ['BTC'],
        };
        let failing = scenario === 'error';
        if (scenario === 'zero') {
          wallet.positions.forEach((position) =>
            Object.assign(position, {
              quantity: 0,
              value: 0,
              status: 'Closed',
              averageCost: null,
              realizedPnl: null,
              unrealizedPnl: null,
              costStatus: 'Unknown',
            }),
          );
          wallet.summary = {
            totalAssets: 0,
            availableThb: 1500,
            holdingCount: 0,
          };
        } else if (scenario === 'missing' || scenario === 'missing-zero') {
          wallet.updatedAt = null;
          wallet.priceUpdatedAt = null;
          wallet.positions = [];
          wallet.summary = {
            totalAssets: null,
            availableThb: null,
            holdingCount: null,
          };
          wallet.performance = null;
          if (scenario === 'missing-zero') {
            wallet.summary = {
              totalAssets: 0,
              availableThb: 0,
              holdingCount: 0,
            };
          }
        } else if (scenario === 'stale') {
          wallet.updatedAt = new Date(
            Date.parse(fixture.at) - 180000,
          ).toISOString();
        } else if (scenario === 'reserved') {
          wallet.summary.availableThb = 800;
        }
        await context.route('**/*', async (route) => {
          const request = route.request(),
            url = new URL(request.url());
          if (url.origin !== 'http://127.0.0.1:3108') {
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
          let body = structuredClone(fixture.data[url.pathname]);
          if (url.pathname === '/api/crypto/portfolio') {
            await route.fulfill({
              status: failing ? 503 : 200,
              json: failing
                ? { error: 'Synthetic wallet unavailable' }
                : wallet,
            });
            return;
          }
          if (url.pathname === '/api/trading') body = trading;
          if (url.pathname === '/api/team/brief')
            body.date = url.searchParams.get('date');
          if (url.pathname === '/api/crypto/risk' && scenario === 'reserved') {
            body.capital.spendableThb = '800';
            body.capital.reservedThb = '200';
          }
          await route.fulfill({
            status: body ? 200 : 404,
            json: body ?? { error: 'Unmocked fixture route' },
          });
        });
        page.on('pageerror', (error) => errors.push(error.message));
        await page.clock.install({ time: new Date(fixture.at) });
        await page.goto(process.env.TEST_URL + '/crypto');
        await page
          .getByRole('button', {
            name: width >= 1024 ? 'Collapse team chat' : 'Expand team chat',
            exact: true,
          })
          .waitFor();
        const collapseChat = page.getByRole('button', {
          name: 'Collapse team chat',
          exact: true,
        });
        if (await collapseChat.isVisible()) {
          await collapseChat.click();
          await page.clock.runFor(500);
          await page
            .getByRole('button', { name: 'Expand team chat' })
            .waitFor();
        }
        const main = page.getByRole('main');
        if (scenario === 'error') {
          await main
            .getByText('Synthetic wallet unavailable', { exact: true })
            .waitFor();
          assert.equal(await main.getByTestId('total-assets').count(), 0);
          assert.equal(
            await main
              .getByText('No assets currently held in this snapshot.', {
                exact: true,
              })
              .count(),
            0,
          );
        } else {
          await main.getByTestId('total-assets').waitFor();
          if (scenario === 'zero') {
            await main
              .getByText('No assets currently held in this snapshot.', {
                exact: true,
              })
              .waitFor();
            assert.equal(
              await main.getByTestId('holding-count').textContent(),
              '0',
            );
            await main
              .getByText('Balance check needed:', {
                exact: false,
              })
              .waitFor();
            const balanceWarning = main
              .locator('summary')
              .filter({ hasText: 'Balance check needed:' });
            const explanation = main.getByText(
              'Holdings use the saved Bitkub wallet observation.',
              { exact: false },
            );
            assert.equal(await explanation.isVisible(), false);
            await balanceWarning.focus();
            await page.keyboard.press('Enter');
            await explanation.waitFor();
            await page.keyboard.press('Enter');
            assert.equal(await explanation.isVisible(), false);
            assert.match(
              await balanceWarning.textContent(),
              /Cost and profit are unverified/,
            );
            await main.getByText('Orders & positions', { exact: true }).click();
            await main
              .getByText('Ledger lots & intents', { exact: false })
              .click();
            await main
              .getByText('BTC: Ledger quantity 0.2', { exact: false })
              .waitFor();
            await main
              .getByRole('button', { name: 'Closed', exact: true })
              .click();
            await main
              .getByRole('button', { name: 'View BTC history', exact: true })
              .click();
            const accounting = main.getByRole('region', {
              name: 'BTC engine cost and performance',
              exact: true,
            });
            await accounting
              .getByText(/Unknown cost remains unknown/)
              .waitFor();
            assert.doesNotMatch(await accounting.textContent(), /0\.00|\+\d/);
            assert.equal(
              await main
                .getByText(/Verified profit|Account profit verified/)
                .count(),
              0,
            );
          } else if (scenario === 'missing' || scenario === 'missing-zero') {
            await main
              .getByText(
                'Wallet holdings unavailable. A verified snapshot is required.',
                { exact: true },
              )
              .waitFor();
            await main
              .getByRole('button', {
                name: 'Snapshot status: Unavailable',
                exact: true,
              })
              .waitFor();
            for (const metric of [
              'total-assets',
              'available-thb',
              'holding-count',
            ]) {
              assert.match(
                await main.getByTestId(metric).textContent(),
                /^[-—]$/,
              );
              assert.doesNotMatch(
                await main.getByTestId(metric).textContent(),
                /0\.00|Holding0/,
              );
            }
          } else if (scenario === 'stale') {
            await main
              .getByRole('button', {
                name: 'Snapshot status: Stale',
                exact: true,
              })
              .waitFor();
            assert.match(
              await main.getByTestId('total-assets').textContent(),
              /12,500\.00/,
            );
          } else if (scenario === 'cached-error') {
            failing = true;
            const response = page.waitForResponse(
              (item) =>
                new URL(item.url()).pathname === '/api/crypto/portfolio',
            );
            await main
              .getByRole('button', { name: 'Refresh', exact: true })
              .click();
            await page.clock.runFor(1000);
            await response;
            await main.getByText(/Showing the last saved snapshot/).waitFor();
            assert.match(
              await main.getByTestId('total-assets').textContent(),
              /12,500\.00/,
            );
          } else if (scenario === 'future-cached') {
            await page.clock.setSystemTime(
              new Date(Date.parse(fixture.at) - 300000),
            );
            const response = page.waitForResponse(
              (item) =>
                new URL(item.url()).pathname === '/api/crypto/portfolio',
            );
            await main
              .getByRole('button', { name: 'Refresh', exact: true })
              .click();
            await page.clock.runFor(1000);
            await response;
            await main
              .getByText(/snapshot timestamp is in the future/)
              .waitFor();
            await main
              .getByText(
                'Wallet holdings unavailable. A verified snapshot is required.',
                { exact: true },
              )
              .waitFor();
            for (const metric of [
              'total-assets',
              'available-thb',
              'holding-count',
            ]) {
              assert.match(
                await main.getByTestId(metric).textContent(),
                /^[-—]$/,
              );
            }
          } else if (scenario === 'reserved') {
            assert.match(
              await main.getByTestId('available-thb').textContent(),
              /800\.00/,
            );
            assert.doesNotMatch(
              await main.getByTestId('available-thb').textContent(),
              /1,000\.00/,
            );
            await main.getByText('Data details', { exact: true }).click();
            await main
              .getByText(/Available THB excludes broker-reserved cash/)
              .waitFor();
          }
        }
        assert(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
          ),
          device + ' ' + scenario + ' overflow',
        );
        if (['zero', 'missing', 'reserved'].includes(scenario)) {
          const target = output + '/wallet-' + scenario + '-' + device + '.png';
          await page.screenshot({ path: target, fullPage: true });
          captures.push(target);
        }
        results.push({ device, scenario, passed: true });
        await context.close();
      }
    }
    assert.deepEqual(writes, []);
    assert.deepEqual(escaped, []);
    assert.deepEqual(errors, []);
    await fs.writeFile(
      output + '/results.json',
      JSON.stringify(
        { results, captures, writes, escaped, errors, syntheticOnly: true },
        null,
        2,
      ),
    );
    console.log(
      'Wallet fixtures: ' +
        results.length +
        ' desktop/mobile scenarios passed; zero vs ledger, stale/missing/errors, clock rollback, reserved exclusion, unknown PnL; no writes or escaped requests.',
    );
  } finally {
    await browser.close();
  }
}
verify().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
