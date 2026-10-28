const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const {
  LocalizationProvider,
} = require('@mui/x-date-pickers/LocalizationProvider');
const { AdapterDayjs } = require('@mui/x-date-pickers/AdapterDayjs');
const fixture = require('./fixtures/workspace-data.cjs');

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
  default: CryptoPage,
} = require('../apps/web/features/crypto/components/crypto-page/CryptoPage.tsx');
const {
  default: TradingBookView,
} = require('../apps/web/features/trading/components/trading-book-view/TradingBookView.tsx');
const {
  default: TradingPanel,
} = require('../apps/web/features/trading/components/trading-panel/TradingPanel.tsx');
const wallet = structuredClone(fixture.data['/api/crypto/portfolio']);
wallet.positions.forEach((position) => {
  Object.assign(position, {
    quantity: 0,
    value: 0,
    status: 'Closed',
    averageCost: null,
    unrealizedPnl: null,
    costStatus: 'Unknown',
  });
});
wallet.summary = { totalAssets: 0, availableThb: 1500, holdingCount: 0 };
wallet.performance = {
  scope: 'confirmedEngineFills',
  realizedPnl: null,
  unrealizedPnl: null,
  totalPnl: null,
  fees: null,
  asOf: fixture.at,
  unreconciledSymbols: ['BTC'],
  notice: 'Synthetic unresolved strategy accounting.',
};
const page = new CryptoPage({});
page.context = {
  history: { get: async () => ({ trades: [] }) },
  portfolio: { get: async () => wallet },
};
page.state = { ...page.state, data: wallet, now: Date.parse(fixture.at) };
const originalFetch = global.fetch;
global.fetch = () => {
  throw Error('Offline presentation checks must never call a service.');
};
const findTradingPanels = (node, matches = []) => {
  if (node === null || node === undefined || typeof node !== 'object') {
    return matches;
  }
  if (node.type === TradingPanel) {
    matches.push(node);
  }
  if (Array.isArray(node)) {
    node.forEach((child) => findTradingPanels(child, matches));
  } else {
    findTradingPanels(node.props?.children, matches);
  }
  return matches;
};
try {
  const pageTree = page.render();
  const tradingPanels = findTradingPanels(pageTree);
  assert.equal(tradingPanels.length, 1);
  assert.strictEqual(tradingPanels[0].props.portfolio, wallet);
  const markup = renderToStaticMarkup(pageTree);
  assert.match(markup, /No assets currently held in this snapshot/);
  assert.match(markup, /data-testid="holding-count"[^]*?>0<\/p>/);
  assert.equal(wallet.positions[0].quantity, 0);
  page.state = {
    ...page.state,
    data: {
      ...wallet,
      updatedAt: null,
      positions: [],
      summary: { totalAssets: null, availableThb: null, holdingCount: null },
    },
  };
  const unavailable = renderToStaticMarkup(page.render());
  assert.match(unavailable, /Wallet holdings unavailable/);
  assert.doesNotMatch(unavailable, /No assets currently held/);
  assert.doesNotMatch(unavailable, /data-testid="holding-count"[^]*?>0<\/p>/);
  page.state.data.summary = {
    totalAssets: 0,
    availableThb: 0,
    holdingCount: 0,
  };
  assert.doesNotMatch(
    renderToStaticMarkup(page.render()),
    /data-testid="holding-count"[^]*?>0<\/p>/,
    'An unobserved snapshot cannot turn placeholder zero into a verified balance.',
  );
  page.state.data.updatedAt = new Date(
    Date.parse(fixture.at) + 300000,
  ).toISOString();
  const future = renderToStaticMarkup(page.render());
  assert.match(future, /Wallet holdings unavailable/);
  assert.doesNotMatch(future, /data-testid="holding-count"[^]*?>0<\/p>/);
  const freshWallet = { ...wallet, updatedAt: new Date().toISOString() };
  const overview = structuredClone(fixture.data['/api/trading']);
  overview.mode = 'stopped';
  overview.live.positions = [
    {
      symbol: 'BTC',
      quantity: '.2',
      costThb: '100',
      openedAt: fixture.at,
      peakBid: '600',
    },
  ];
  const renderTrading = (portfolio) => {
    const panel = new TradingPanel({ date: fixture.date, portfolio });
    panel.state = { ...panel.state, overview };
    return renderToStaticMarkup(
      React.createElement(
        LocalizationProvider,
        { dateAdapter: AdapterDayjs },
        panel.render(),
      ),
    );
  };
  for (const performance of [
    null,
    undefined,
    { ...wallet.performance, unreconciledSymbols: [] },
  ]) {
    assert.doesNotMatch(
      renderTrading({
        ...freshWallet,
        positions: freshWallet.positions.map((position) =>
          position.symbol === 'BTC' ? { ...position, quantity: 0.2 } : position,
        ),
        performance,
      }),
      /Balance review needed|holdings differ from the saved strategy/,
    );
  }
  const freshTrading = renderTrading(freshWallet);
  assert.match(freshTrading, /Balance review needed/);
  assert.match(freshTrading, /BTC holdings differ from the saved strategy/);
  assert.match(freshTrading, /cost and profit remain unverified/);
  assert.equal(wallet.positions[0].quantity, 0);
  assert.equal(overview.live.positions[0].quantity, '.2');
  for (const updatedAt of [
    null,
    undefined,
    new Date(Date.now() - 180000).toISOString(),
    new Date(Date.now() + 180000).toISOString(),
  ]) {
    assert.doesNotMatch(
      renderTrading({ ...wallet, updatedAt }),
      /Balance review needed|holdings differ from the saved strategy/,
    );
  }
  const matched = {
    ...freshWallet,
    positions: freshWallet.positions.map((position) =>
      position.symbol === 'BTC' ? { ...position, quantity: 0.2 } : position,
    ),
  };
  assert.doesNotMatch(
    renderTrading(matched),
    /Balance review needed|holdings differ from the saved strategy/,
  );
  const historicalOnly = renderTrading({
    ...matched,
    performance: { ...freshWallet.performance, unreconciledSymbols: ['BTC'] },
  });
  assert.doesNotMatch(
    historicalOnly,
    /Balance review needed|holdings differ from the saved strategy/,
  );
  const book = renderToStaticMarkup(
    React.createElement(TradingBookView, { overview }),
  );
  assert.match(book, /Strategy ledger/);
  assert.match(
    book,
    /Accounting lots can differ from wallet holdings after external trades/,
  );
  assert.match(book, /1 lot/);
  assert.match(book, /Tracked intents/);
  assert.doesNotMatch(book, /Live trading balances|Open orders<|1 positions/);
  assert.equal(overview.live.positions[0].quantity, '.2');
  console.log(
    'Wallet source separation: external-sale zero, unresolved strategy lot, legacy/matched states, explicit intent labels and no I/O passed.',
  );
} finally {
  global.fetch = originalFetch;
}
