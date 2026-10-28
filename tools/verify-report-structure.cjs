const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
for (const extension of ['.ts', '.tsx']) {
  require.extensions[extension] = (module, filename) =>
    module._compile(
      ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
        compilerOptions: {
          module: ts.ModuleKind.CommonJS,
          target: ts.ScriptTarget.ES2022,
          jsx: ts.JsxEmit.ReactJSX,
          esModuleInterop: true,
        },
      }).outputText,
      filename,
    );
}
const {
  default: BriefMessage,
} = require('../apps/web/features/team/components/brief-message/BriefMessage.tsx');
const plain =
  'adaptive-intraday-v2 | stopped\nตลาดจำลองยังยืนยันไม่ได้\nรอแท่งและข้อมูลใหม่; ไม่ส่งคำสั่ง\n\nอ้างอิง asOf 2026-10-20T17:18:41.1927051Z ข้อมูล 15m/60m ไม่ทราบ\n\nSource fetched: 17:17:39–40Z\n\n• BTC/THB: ปิด 100.00; ไม่ยืนยัน breakout\n• TEST/THB: ข้อมูลขาด; volume ไม่ทราบ\n\nWallet จำลองเป็นศูนย์; engine lot ไม่ตรง; P&L Unknown\n\nINTRA-01 Pending: รอหลักฐานใหม่ ไม่ใช่อำนาจเทรด';
const output = plain + '\n\nPolicy hash: synthetic-fixture';
const render = (text, role = 'intraday') =>
  renderToStaticMarkup(
    React.createElement(BriefMessage, {
      output: text,
      role,
      idPrefix: 'synthetic-report-',
    }),
  );
const markup = render(output);
assert.match(markup, /Recorded summary/);
assert.match(markup, /Stopped · recorded/);
assert.match(markup, /BTC\/THB/);
assert.match(markup, /TEST\/THB/);
assert.match(markup, /Source metadata/);
assert.match(markup, /Source freshness/);
assert.match(markup, /Original report/);
assert.doesNotMatch(markup, /<script|<iframe|<img/);
const {
  IntradayReportLayout,
} = require('../apps/web/features/team/presentation/IntradayReportLayout.ts');
const layout = IntradayReportLayout.parse(output);
assert(layout);
assert.equal(layout.mode, 'stopped');
for (const warning of ['stale', 'expired', 'delayed', 'outdated']) {
  const text = `adaptive-intraday-v2 | stopped\nRecorded summary\n\nSource fetched: 05:54Z; quotes ${warning}.`;
  assert.equal(IntradayReportLayout.parse(text).sections[0].metadata, false);
  assert.match(render(text), /Source freshness/);
  assert.doesNotMatch(render(text), /Source metadata/);
}
assert.equal(layout.summary[1], 'รอแท่งและข้อมูลใหม่; ไม่ส่งคำสั่ง');
assert.equal(layout.sections.filter((section) => section.metadata).length, 1);
assert(
  layout.sections.some(
    (section) => section.heading === 'TEST/THB' && !section.metadata,
  ),
);
assert(
  layout.sections.some(
    (section) => section.text.includes('P&L Unknown') && !section.metadata,
  ),
);
assert.equal(
  IntradayReportLayout.parse(
    'adaptive-intraday-v2 | stopped\n\n# Summary\n\n- A Markdown report',
  ),
  null,
);
for (const text of [
  'plain report without a contract',
  '',
  'adaptive-intraday-v2 | mystery\nUnknown',
  '# Header\n\n- Plain Markdown',
]) {
  assert.equal(IntradayReportLayout.parse(text), null);
  assert.doesNotMatch(render(text), /Recorded summary/);
}
const hostile =
  'adaptive-intraday-v2 | stopped\nSynthetic summary\n\n<script>alert(1)</script>\n\n![image](https://example.com/tracker)';
assert.doesNotMatch(render(hostile), /<script|<img/);
const long = output + '\n\n' + 'LongSyntheticText'.repeat(700);
assert.match(render(long), /Original report/);
assert.match(render('# Markdown heading\n\n- Safe item', 'analyst'), /<h4/);
console.log(
  'Report structure: literal summary/mode, per-market sections, visible uncertainties, metadata/raw disclosure, malformed/Markdown fallback and safe long content passed.',
);
