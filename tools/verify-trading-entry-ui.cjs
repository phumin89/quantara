const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const path = require('node:path');
const { chromium } = require('playwright');
const fixture = require('./fixtures/workspace-data.cjs');
const staticBuildAsset = require('./fixtures/static-build-assets.cjs');

for (const extension of ['.ts', '.tsx']) {
  require.extensions[extension] = (module, filename) => {
    const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2022,
        jsx: ts.JsxEmit.ReactJSX,
        esModuleInterop: true,
      },
    });
    module._compile(output.outputText, filename);
  };
}

const {
  tradingOverviewSchema,
} = require('../apps/web/features/trading/contracts/TradingOverview.ts');
const {
  default: TradingBookView,
} = require('../apps/web/features/trading/components/trading-book-view/TradingBookView.tsx');
const {
  default: DailyBriefConversation,
} = require('../apps/web/features/team/components/daily-brief-conversation/DailyBriefConversation.tsx');

const rules = {
  version: 'ema20-50-breakout-v1',
  symbols: ['BTC'],
  allocatedThb: 1000,
  perOrderThb: 100,
  maxPositions: 2,
  dailyLossThb: 100,
  maxSpreadBps: 50,
  slippageBps: 20,
  maxDataAgeSeconds: 90,
  takeProfitBps: 200,
  stopLossBps: 100,
  trailingStopBps: 100,
  timeExitMinutes: 120,
  cooldownMinutes: 15,
  orderLifetimeMinutes: 15,
  feeBps: 25,
  volumeRatio: 1.5,
};
const book = {
  cashThb: '1000',
  equityThb: null,
  realizedGrossThb: null,
  realizedNetThb: null,
  feesThb: '0',
  unrealizedThb: null,
  positions: [],
  orders: [],
};
const base = {
  accountKey: 'fixture',
  mode: 'paper',
  rules,
  rulesHash: 'hash',
  mandate: null,
  emergencyStopped: false,
  heartbeatAt: null,
  fault: null,
  liveActivationAvailable: false,
  paper: book,
  live: book,
  reports: [],
};

assert.equal(
  tradingOverviewSchema.safeParse(base).success,
  true,
  'legacy payload remains valid',
);
const order = {
  id: 'cancelled-before-send',
  symbol: 'BTC',
  side: 'buy',
  amount: '80',
  originalAmount: '100',
  limitPrice: '2',
  remaining: '80',
  status: 'cancelled',
  createdAt: '2026-10-16T03:00:00Z',
  brokerOrderId: null,
  reason: 'signal reason',
  lastExecutionMessage: 'Not submitted: canceled before broker submission.',
  lastExecutionAt: '2026-10-16T03:01:00Z',
};
const withOrder = {
  ...base,
  mode: 'stopped',
  live: { ...book, orders: [order] },
};
assert.equal(
  tradingOverviewSchema.safeParse(withOrder).success,
  true,
  'new diagnostics validate',
);
const markup = renderToStaticMarkup(
  React.createElement(TradingBookView, { overview: withOrder }),
);
assert.match(markup, /Recent completed order history/);
assert.match(markup, /Canceled before send/);
assert.match(markup, /Not submitted: canceled before broker submission/);
assert.match(markup, /Adjusted from 100/);
assert.match(markup, /Strategy reason: signal reason/);
const uncertainCancellation = renderToStaticMarkup(
  React.createElement(TradingBookView, {
    overview: {
      ...withOrder,
      live: { ...book, orders: [{ ...order, cancellationState: 'unknown' }] },
    },
  }),
);
assert.match(uncertainCancellation, /Active orders/);
assert.doesNotMatch(uncertainCancellation, /Recent order history/);
assert.doesNotMatch(uncertainCancellation, /Canceled before send/);
const pending = {
  ...order,
  id: 'pending',
  status: 'pending',
  brokerOrderId: null,
  lastExecutionMessage: 'Awaiting broker confirmation.',
};
const pendingMarkup = renderToStaticMarkup(
  React.createElement(TradingBookView, {
    overview: {
      ...base,
      mode: 'stopped',
      live: { ...book, orders: [pending] },
    },
  }),
);
assert.match(pendingMarkup, /Awaiting broker confirmation/);
assert.equal(
  tradingOverviewSchema.safeParse({
    ...withOrder,
    live: { ...book, orders: [{ ...order, originalAmount: 'not-a-decimal' }] },
  }).success,
  false,
);
assert.equal(
  tradingOverviewSchema.safeParse({
    ...withOrder,
    live: { ...book, orders: [{ ...order, lastExecutionAt: 'not-a-time' }] },
  }).success,
  false,
);
assert.equal(
  tradingOverviewSchema.safeParse({
    ...withOrder,
    live: {
      ...book,
      orders: [{ ...order, lastExecutionMessage: 'x'.repeat(501) }],
    },
  }).success,
  false,
);

const researchMarkup = renderToStaticMarkup(
  React.createElement(DailyBriefConversation, {
    active: true,
    brief: { date: fixture.date, runs: [fixture.run] },
    date: fixture.date,
    loading: false,
    error: null,
  }),
);
assert.match(researchMarkup, /Research snapshot updated/);
assert.match(researchMarkup, /09:59/);

async function verifyBrowser() {
  const directory = process.env.TRADING_ENTRY_CAPTURE_DIR;
  assert(directory, 'Set a private capture directory.');
  fs.mkdirSync(directory, { recursive: true });
  const browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHANNEL,
    headless: true,
  });
  const origin = 'http://127.0.0.1:3108';
  const results = [];
  try {
    for (const width of [1440, 820, 390]) {
      const context = await browser.newContext({
        viewport: { width, height: 1000 },
        serviceWorkers: 'block',
        reducedMotion: 'reduce',
      });
      const page = await context.newPage();
      page.setDefaultTimeout(10000);
      await page.clock.install({ time: new Date(fixture.at) });
      const data = structuredClone(fixture.data);
      const trading = data['/api/trading'];
      trading.mode = 'stopped';
      trading.liveActivationAvailable = true;
      trading.live.orders = [pending, order];
      trading.rules = {
        ...trading.rules,
        version: 'adaptive-intraday-v2',
        symbols: [],
        slippageBps: 23,
        feeBps: 27,
        volumeRatio: 1.7,
        adaptive: {
          universeVersion: 'bitkub-thb-spot-v1',
          includeFutureDeposits: true,
          excludedSymbols: ['USDT', 'USDC', 'DAI'],
          risk: {
            profile: 'aggressive',
            maximumExposureFraction: 0.4,
            maximumConcentrationFraction: 0.15,
            riskToStopFraction: 0.02,
            atrMultiplier: 1,
            maximumSpreadFraction: 0.005,
            estimatedFeeRate: 0.0027,
            maximumDataAgeSeconds: 90,
            maximumSignalAgeSeconds: 600,
          },
          dailyLossFraction: 0.03,
          dailyProfitTargetThb: 500,
        },
      };
      const violations = [];
      tradingOverviewSchema.parse(trading);
      const errors = [];
      const writes = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await context.route('**/*', async (route) => {
        const address = new URL(route.request().url());
        if (address.origin !== origin) {
          violations.push(route.request().method() + ' ' + address.pathname);
          return route.abort();
        }
        if (route.request().method() !== 'GET') {
          if (
            route.request().method() === 'POST' &&
            address.pathname === '/api/trading/activate'
          ) {
            const payload = route.request().postDataJSON();
            writes.push(payload);
            return route.fulfill({ json: data['/api/trading'] });
          }
          violations.push(route.request().method() + ' ' + address.pathname);
          return route.abort();
        }
        if (!address.pathname.startsWith('/api/'))
          return staticBuildAsset(route, address);
        assert(
          Object.hasOwn(data, address.pathname),
          'Unknown fixture API ' + address.pathname,
        );
        return route.fulfill({ json: data[address.pathname] });
      });
      await page.goto(origin + '/crypto');
      await page
        .getByRole('button', { name: 'Review & enable', exact: true })
        .click();
      const mandate = page.getByRole('dialog');
      const aiCheckbox = mandate.getByRole('checkbox', {
        name: 'Let AI manage strategy',
        exact: true,
      });
      assert.equal(
        await aiCheckbox.isChecked(),
        false,
        `AI opt-in defaults off at ${width}px`,
      );
      await mandate.getByRole('button', { name: '1 day', exact: true }).click();
      await aiCheckbox.check();
      assert.equal(
        await aiCheckbox.isChecked(),
        true,
        `AI opt-in is selectable at ${width}px`,
      );
      await mandate
        .getByRole('button', { name: 'Cancel', exact: true })
        .click();
      await mandate.waitFor({ state: 'hidden' });
      await page
        .getByRole('button', { name: 'Review & enable', exact: true })
        .click();
      const reopened = page.getByRole('dialog');
      const reopenedCheckbox = reopened.getByRole('checkbox', {
        name: 'Let AI manage strategy',
        exact: true,
      });
      assert.equal(
        await reopenedCheckbox.isChecked(),
        false,
        `AI opt-in resets after cancel/reopen at ${width}px`,
      );
      await reopened
        .getByRole('button', { name: '1 day', exact: true })
        .click();
      await reopenedCheckbox.check();
      await reopenedCheckbox.scrollIntoViewIfNeeded();
      await page.screenshot({
        path: path.join(directory, `ai-managed-review-${width}.png`),
      });
      await reopened
        .getByRole('button', { name: 'Enable AI-managed trading', exact: true })
        .click();
      await page.waitForTimeout(100);
      assert.equal(
        writes.length,
        1,
        `activation payload captured at ${width}px`,
      );
      assert.equal(writes[0].aiManaged, true);
      assert.equal(writes[0].acknowledgement, 'enable-ai-managed-trading');
      await page.screenshot({
        path: path.join(directory, `ai-managed-${width}.png`),
      });
      await page.screenshot({
        path: path.join(directory, `before-settings-${width}.png`),
      });
      await page
        .getByRole('button', { name: 'Trading settings', exact: true })
        .click();
      const dialog = page.getByRole('dialog', { name: 'Trading settings' });
      await dialog
        .getByRole('heading', { name: 'Saved risk limits' })
        .waitFor();
      assert.equal(await dialog.locator('input:visible').count(), 0);
      assert.equal(
        await dialog
          .getByRole('button', { name: 'Save overrides and disarm live' })
          .isVisible(),
        false,
      );
      const fields = await dialog.locator('form').evaluate((form) => {
        const technical = [...form.querySelectorAll('details')].find((item) =>
          item
            .querySelector('summary')
            ?.textContent.includes('Advanced overrides'),
        );
        const names = [
          'maximumExposureFraction',
          'maximumConcentrationFraction',
          'riskToStopFraction',
          'dailyLossFraction',
        ];
        return {
          values: [...new FormData(form).entries()],
          open: technical?.open,
          insideOverrides: names.every(
            (name) =>
              form.querySelector(`[name="${name}"]`)?.closest('details') ===
              technical,
          ),
          count: names.map(
            (name) => form.querySelectorAll(`[name="${name}"]`).length,
          ),
          fits: form.scrollWidth <= form.clientWidth + 1,
        };
      });
      assert.equal(fields.open, false);
      assert.equal(fields.insideOverrides, true);
      assert.deepEqual(fields.count, [1, 1, 1, 1]);
      const values = Object.fromEntries(fields.values);
      assert.equal(values.maximumExposureFraction, '40');
      assert.equal(values.maximumConcentrationFraction, '15');
      assert.equal(values.riskToStopFraction, '2');
      assert.equal(values.dailyLossFraction, '3');
      assert.equal(values.slippageBps, '23');
      assert.equal(values.feeBps, '27');
      assert.equal(values.volumeRatio, '1.7');
      assert.equal(
        new Set(fields.values.map(([name]) => name)).size,
        fields.values.length,
      );
      assert(fields.fits, 'Settings must fit the viewport.');
      await page.screenshot({
        path: path.join(directory, `settings-${width}.png`),
      });
      await dialog
        .locator('summary')
        .filter({ hasText: /^Advanced overrides$/ })
        .press('Enter');
      await dialog.getByRole('heading', { name: 'Your risk limits' }).waitFor();
      for (const name of [
        'maximumExposureFraction',
        'maximumConcentrationFraction',
        'riskToStopFraction',
        'dailyLossFraction',
      ]) {
        assert.equal(
          await dialog.locator(`input[name="${name}"]`).isVisible(),
          true,
        );
      }
      await dialog
        .getByRole('button', { name: 'Close trading settings' })
        .click();
      await page.getByText('Orders & positions', { exact: true }).click();
      await page
        .getByText('Awaiting broker confirmation.', { exact: false })
        .first()
        .waitFor();
      await page.screenshot({
        path: path.join(directory, `orders-${width}.png`),
      });
      await page
        .getByRole('button', { name: 'Expand team chat', exact: true })
        .click();
      await page
        .getByText('Open Trading to view execution and AI strategy status.', {
          exact: true,
        })
        .waitFor();
      await page
        .getByRole('link', { name: 'View live trading', exact: true })
        .waitFor();
      await page.screenshot({
        path: path.join(directory, `research-${width}.png`),
      });
      assert.deepEqual(violations, []);
      assert.deepEqual(errors, []);
      results.push({
        width,
        savedFormValues: true,
        aiManagedOptIn: true,
        noOverflow: true,
        researchProvenance: true,
      });
      await context.close();
    }
  } finally {
    await browser.close();
  }
  fs.writeFileSync(
    path.join(directory, 'results.json'),
    JSON.stringify(results, null, 2),
  );
  console.log(
    'Trading entry UI: legacy/new contracts, execution diagnostics, history, research provenance, AI opt-in payload and real rendered FormData passed at 1440/820/390px; isolated fixture GETs plus intercepted activation POST only.',
  );
}
verifyBrowser().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
