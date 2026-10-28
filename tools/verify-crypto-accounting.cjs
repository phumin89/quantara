const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');

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
  portfolioSchema,
} = require('../apps/web/features/crypto/contracts/PortfolioResponse.ts');
const verified = {
  symbol: 'TEST',
  name: 'Fixture holding',
  status: 'Holding',
  quantity: 2,
  averageCost: 90.25,
  currentPrice: 100,
  value: 200,
  unrealizedPnl: 19.5,
  realizedPnl: -12.34,
  costStatus: 'Verified',
};
const performance = {
  scope: 'confirmedEngineFills',
  realizedPnl: -12.34,
  unrealizedPnl: 19.5,
  totalPnl: 7.16,
  fees: 0.75,
  asOf: '2026-10-19T11:20:00+07:00',
  unreconciledSymbols: [],
  notice: 'Fixture confirmed fills; future exit fees excluded.',
};
const portfolio = {
  positions: [verified],
  summary: { totalAssets: 200, availableThb: 1500, holdingCount: 1 },
  performance,
  updatedAt: '2026-10-19T11:20:00+07:00',
  priceUpdatedAt: '2026-10-19T11:20:00+07:00',
  historyUpdatedAt: '2026-10-19T11:20:00+07:00',
  historyCoverage: 'Fixture limited exchange history.',
  syncing: false,
  completedSymbols: 0,
  totalSymbols: 0,
  error: null,
};
assert.deepEqual(portfolioSchema.parse(portfolio), portfolio);

const {
  default: PortfolioRow,
} = require('../apps/web/features/crypto/components/portfolio-row/PortfolioRow.tsx');
const {
  default: EnginePerformanceSummary,
} = require('../apps/web/features/crypto/components/engine-performance-summary/EnginePerformanceSummary.tsx');
const row = (item, expanded = true) =>
  renderToStaticMarkup(
    React.createElement(PortfolioRow, {
      item,
      expanded,
      onOpenHistory() {},
      history: [],
      historyLoading: false,
      historyError: null,
      onRetry() {},
    }),
  );
const summary = (data) =>
  renderToStaticMarkup(React.createElement(EnginePerformanceSummary, { data }));
assert.match(row(verified), /฿90\.25/);
assert.match(row(verified), /\+฿19\.50/);
assert.match(row(verified), /-฿12\.34/);
assert.doesNotMatch(row(verified), />Unknown cost</);
assert.doesNotMatch(row(verified, false), /19\.50|12\.34|90\.25/);
assert.match(
  row({ ...verified, averageCost: null, costStatus: 'Unknown' }, false),
  /Cost unavailable/,
);
assert.match(summary(portfolio), /\+฿7\.16/);
assert.match(summary(portfolio), /฿0\.75/);
assert.match(summary(portfolio), /Account-wide profit unavailable/);
assert.match(summary(portfolio), /Fixture confirmed fills/);

const unknown = {
  ...verified,
  averageCost: null,
  unrealizedPnl: null,
  realizedPnl: null,
  costStatus: 'Unknown',
};
const legacy = { ...portfolio, positions: [unknown] };
delete legacy.performance;
assert.deepEqual(portfolioSchema.parse(legacy), legacy);
assert.match(row(unknown), /Unknown cost/);
assert.doesNotMatch(row(unknown), /฿0\.00/);
assert.match(summary(legacy), /Engine performance unavailable/);
assert.doesNotMatch(summary(legacy), /฿0\.00/);
assert.equal(
  portfolioSchema.safeParse({ ...legacy, performance: null }).success,
  true,
);

const incomplete = {
  ...portfolio,
  performance: {
    ...performance,
    unrealizedPnl: null,
    totalPnl: null,
    fees: null,
    asOf: null,
    unreconciledSymbols: ['OLD'],
  },
};
assert.deepEqual(portfolioSchema.parse(incomplete), incomplete);
assert.match(summary(incomplete), /-฿12\.34/);
assert.match(summary(incomplete), /OLD/);
assert.doesNotMatch(summary(incomplete), /฿0\.00/);
assert.match(row({ ...verified, unrealizedPnl: 0, realizedPnl: 0 }), /฿0\.00/);
for (const invalid of [
  { ...performance, scope: 'wholeAccount' },
  { ...performance, asOf: 'not-a-date' },
  { ...performance, realizedPnl: '12' },
  { ...performance, fees: -1 },
]) {
  assert.equal(
    portfolioSchema.safeParse({ ...portfolio, performance: invalid }).success,
    false,
  );
}
console.log(
  'Crypto accounting: verified values, losses, true zero, unknowns, legacy payloads and invalid evidence passed.',
);
