const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

// Load only the pure feature modules; application type-checking runs separately.
require.extensions['.ts'] = (module, filename) => {
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  });
  module._compile(output.outputText, filename);
};
const feature = path.resolve('apps/web/features/crypto');
const { ConnectionStatusPresenter } = require(
  path.join(feature, 'presentation/ConnectionStatusPresenter.ts'),
);
const { ConnectionState } = require(
  path.join(feature, 'contracts/ConnectionState.ts'),
);
const { PositionStatus } = require(
  path.join(feature, 'contracts/PositionStatus.ts'),
);
const { PortfolioFilter } = require(
  path.join(feature, 'contracts/PortfolioFilter.ts'),
);
const { default: filterPositions } = require(
  path.join(feature, 'utils/filterPositions.ts'),
);

const timestamp = '2026-01-01T00:00:00Z';
assert.equal(
  new ConnectionStatusPresenter('Failed', true, timestamp).state,
  ConnectionState.Error,
);
assert.equal(
  new ConnectionStatusPresenter(null, true, timestamp).state,
  ConnectionState.Syncing,
);
assert.equal(
  new ConnectionStatusPresenter(null, false, timestamp).state,
  ConnectionState.Connected,
);
assert.equal(
  new ConnectionStatusPresenter(null, false, null).state,
  ConnectionState.Checking,
);
assert.deepEqual(
  new ConnectionStatusPresenter(null, false, timestamp).appearance,
  {
    label: ConnectionState.Connected,
    color: '#15803d',
    background: '#f0fdf4',
  },
);
const positions = [
  { symbol: 'Z', status: PositionStatus.Closed },
  { symbol: 'B', status: PositionStatus.Holding },
  { symbol: 'A', status: PositionStatus.Holding },
];
assert.deepEqual(
  filterPositions(positions, PortfolioFilter.All).map((item) => item.symbol),
  ['A', 'B', 'Z'],
);
assert.deepEqual(
  filterPositions(positions, PortfolioFilter.Holding).map(
    (item) => item.symbol,
  ),
  ['A', 'B'],
);
assert.deepEqual(
  filterPositions(positions, PortfolioFilter.Closed).map((item) => item.symbol),
  ['Z'],
);
assert.deepEqual(
  positions.map((item) => item.symbol),
  ['Z', 'B', 'A'],
);
console.log(
  'Presenter status priority and enum filters preserve behavior without mutating source data.',
);
