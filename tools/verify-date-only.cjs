const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
require.extensions['.ts'] = (module, file) =>
  module._compile(
    ts.transpileModule(fs.readFileSync(file, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        esModuleInterop: true,
        target: ts.ScriptTarget.ES2022,
      },
    }).outputText,
    file,
  );
const {
  BangkokDate,
} = require('../apps/web/shared/presentation/BangkokDate.ts');
for (const zone of ['UTC', 'America/Los_Angeles', 'Pacific/Auckland']) {
  process.env.TZ = zone;
  for (const value of ['2026-01-01', '2026-10-20', '2024-02-29'])
    assert.equal(
      BangkokDate.format(BangkokDate.parse(value)),
      value,
      `${zone}: date-only must not shift.`,
    );
  for (const value of [
    '2026-02-29',
    '2026-02-30',
    '2026-13-01',
    '2026-1-1',
    '2026-10-20T00:00:00Z',
    '',
  ])
    assert.equal(BangkokDate.parse(value), null);
}
assert.equal(BangkokDate.format(null), null);
console.log(
  'Date-only: Bangkok day stays stable across system timezones; leap years, malformed dates and null validated.',
);
