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
const { default: PositionFilter } = require(
  path.join(feature, 'utils/PositionFilter.ts'),
);
const { default: FormatTime } = require(
  path.join(feature, 'utils/FormatTime.ts'),
);
const originalTimezone = process.env.TZ;
for (const timezone of ['UTC', 'America/New_York', 'Asia/Tokyo']) {
  process.env.TZ = timezone;
  assert.equal(
    FormatTime.format('2026-10-20T03:00:00Z'),
    'Oct 20, 2026, 10:00',
  );
}
if (originalTimezone === undefined) delete process.env.TZ;
else process.env.TZ = originalTimezone;
assert.equal(FormatTime.format(null), '—');
const {
  default: TradingFormatting,
} = require('../apps/web/features/trading/presentation/TradingFormatting.ts');
for (const [value, expected] of [
  [null, 'Unknown'],
  ['0', '฿0'],
  ['-12.5', '฿-12.5'],
  ['0001.5000', '฿1.5'],
  ['1.005', '฿1.01'],
  ['1.0049999999999999999999999999', '฿1'],
  ['-1.0049999999999999999999999999', '฿-1'],
  ['999.995', '฿1,000'],
  ['9007199254740993', '฿9,007,199,254,740,993'],
  ['79228162514264337593543950335', '฿79,228,162,514,264,337,593,543,950,335'],
  ['1e3', '฿1,000'],
  ['', 'Unknown'],
  ['not-a-number', 'Unknown'],
  ['Infinity', 'Unknown'],
]) {
  assert.equal(
    TradingFormatting.money(value),
    expected,
    `Trading money: ${String(value)}`,
  );
}
for (const [values, expected] of [
  [[], '฿0'],
  [['0.004', '0.004'], '฿0.01'],
  [['9007199254740993', '0.005'], '฿9,007,199,254,740,993.01'],
  [['-1.5', '1.25'], '฿-0.25'],
  [['0.0100', '-0.01'], '฿0'],
  [['invalid'], 'Unknown'],
  [['1e3'], 'Unknown'],
]) {
  assert.equal(
    TradingFormatting.money(TradingFormatting.sum(values)),
    expected,
    `Trading cost sum: ${JSON.stringify(values)}`,
  );
}

const timestamp = '2026-01-01T00:00:00Z';
const snapshotClock = Date.parse(timestamp) + 1000;
assert.equal(
  new ConnectionStatusPresenter('Failed', true, timestamp, snapshotClock).state,
  ConnectionState.Error,
);
assert.equal(
  new ConnectionStatusPresenter(null, true, timestamp, snapshotClock).state,
  ConnectionState.Syncing,
);
assert.equal(
  new ConnectionStatusPresenter(null, false, timestamp, snapshotClock).state,
  ConnectionState.Connected,
);
assert.equal(
  new ConnectionStatusPresenter(null, false, null, snapshotClock).state,
  ConnectionState.Checking,
);
assert.deepEqual(
  new ConnectionStatusPresenter(null, false, timestamp, snapshotClock)
    .appearance,
  {
    label: ConnectionState.Connected,
    color: 'var(--color-success)',
    background: 'var(--surface-success)',
  },
);
const positions = [
  { symbol: 'Z', status: PositionStatus.Closed },
  { symbol: 'B', status: PositionStatus.Holding },
  { symbol: 'A', status: PositionStatus.Holding },
];
assert.equal(
  new ConnectionStatusPresenter(null, false, timestamp, snapshotClock + 120000)
    .state,
  ConnectionState.Stale,
);
assert.equal(
  new ConnectionStatusPresenter(null, false, null, snapshotClock, true).state,
  ConnectionState.Unavailable,
);
assert.equal(
  new ConnectionStatusPresenter(null, false, 'not-a-date', snapshotClock, true)
    .state,
  ConnectionState.Unavailable,
);
assert.equal(
  new ConnectionStatusPresenter(
    null,
    false,
    timestamp,
    Date.parse(timestamp) - 30001,
    true,
  ).state,
  ConnectionState.Unavailable,
);
assert.deepEqual(
  PositionFilter.apply(positions, PortfolioFilter.All).map(
    (item) => item.symbol,
  ),
  ['A', 'B', 'Z'],
);
assert.deepEqual(
  PositionFilter.apply(positions, PortfolioFilter.Holding).map(
    (item) => item.symbol,
  ),
  ['A', 'B'],
);
assert.deepEqual(
  PositionFilter.apply(positions, PortfolioFilter.Closed).map(
    (item) => item.symbol,
  ),
  ['Z'],
);
assert.deepEqual(
  positions.map((item) => item.symbol),
  ['Z', 'B', 'A'],
);
console.log(
  'Presenter status priority and enum filters preserve behavior without mutating source data.',
);
