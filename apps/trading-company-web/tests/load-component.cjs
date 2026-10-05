const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');
module.exports = function load(file) {
  const compiled = ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX } }).outputText;
  const loaded = { exports: {} };
  const localRequire = name => {
    if (!name.startsWith('.')) return require(name);
    const base = path.resolve(path.dirname(file), name);
    return module.exports(fs.existsSync(base + '.ts') ? base + '.ts' : base + '.tsx');
  };
  new Function('require', 'module', 'exports', compiled)(localRequire, loaded, loaded.exports);
  return loaded.exports;
};
