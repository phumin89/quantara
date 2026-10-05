const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const load = require('./load-component.cjs');

test('homepage is the single Live application surface', () => {
  const file = path.join(__dirname, '../app/page.tsx');
  const compiled = ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX } }).outputText;
  const loaded = { exports: {} };
  const localRequire = name => name === './LiveAccountPanel' ? { LiveOfficeWorkspace: () => React.createElement('main', { className: 'live-workspace' }, React.createElement('h2', null, 'Live · สำนักงาน')) } : name === 'react/jsx-runtime' ? { jsx: React.createElement, jsxs: (type, props) => React.createElement(type, props, ...(props.children || [])), Fragment: React.Fragment } : {};
  new Function('require', 'module', 'exports', compiled)(localRequire, loaded, loaded.exports);
  const { default: Home } = loaded.exports;
  const html = renderToStaticMarkup(React.createElement(Home));
  assert.match(html, /Live · สำนักงาน/);
  assert.doesNotMatch(html, /Sandbox|เงินจำลอง|เงินและพอร์ต/i);
});
