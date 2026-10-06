const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');

function loadShell(pathname) {
  const file = path.join(__dirname, '../app/AppShell.tsx');
  const source = fs.readFileSync(file, 'utf8');
  const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX } }).outputText;
  const nextLink = ({ href, children, ...props }) => React.createElement('a', { href, ...props }, children);
  const loaded = { exports: {} };
  const localRequire = name => name === 'next/link' ? { default: nextLink } : name === 'next/navigation' ? { usePathname: () => pathname } : name === './LiveAccountPanel' ? { LiveConnectionBadge: () => React.createElement('span', null, 'status') } : name === 'react/jsx-runtime' ? require('react/jsx-runtime') : {};
  new Function('require', 'module', 'exports', compiled)(localRequire, loaded, loaded.exports);
  return loaded.exports.AppShell;
}

test('shell renders real routes and marks each active page', () => {
  for (const [route, title] of [['/', 'ภาพรวมทีม'], ['/account', 'บัญชีและสินทรัพย์'], ['/trading', 'การเทรด']]) {
  const Shell = loadShell(route);
  const html = renderToStaticMarkup(React.createElement(Shell, null, React.createElement('p', null, 'content')));
  assert.match(html, /href="\/"/); assert.match(html, /href="\/account"/); assert.match(html, /href="\/trading"/);
  const activeLink = html.match(/<a[^>]*aria-current="page"[^>]*>/)?.[0];
  assert.ok(activeLink?.includes(`href="${route}"`));
  assert.ok(html.includes(`<h1>${title}</h1>`)); assert.match(html, /ข้ามไปยังเนื้อหาหลัก/);
  assert.equal((html.match(/<h1>/g) || []).length, 1);
  assert.match(html, /href="\/"[^>]*>[\s\S]*?สำนักงาน<\/a>/);
  assert.equal((html.match(/aria-current="page"/g) || []).length, 1);
  }
});
